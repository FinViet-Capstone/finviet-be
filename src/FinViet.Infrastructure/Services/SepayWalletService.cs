using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.ExternalServices.SePay;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotFoundException = FinViet.Application.Common.Exceptions.NotFoundException;
using ValidationException = FinViet.Application.Exceptions.ValidationException;

namespace FinViet.Infrastructure.Services;

internal sealed class SepayWalletService : ISepayWalletService
{
    private const string SepayWalletType = "sepay_linked";
    private const string BasicWalletType = "basic";
    private const string SepayEntryMethod = "sepay_sync";
    private const string StaticAuthMode = "static";
    private const string OAuthAuthMode = "oauth";
    private const int MaximumWalletsPerCustomer = 10;

    /// <summary>
    /// SePay rejects an authorization code the second time it is presented, but the client needs
    /// two calls (list accounts, then link). Caching the exchanged token against the code lets the
    /// pair work without the client having to re-authorize.
    /// </summary>
    private static readonly TimeSpan CodeExchangeCacheLifetime = TimeSpan.FromMinutes(5);

    private readonly FinVietDbContext _db;
    private readonly ISepayClient _client;
    private readonly ISepayTokenProtector _tokenProtector;
    private readonly ISepayLinkStateProtector _stateProtector;
    private readonly IAiCategorizationService _categorizationService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SepayWalletService> _logger;
    private readonly SepayOptions _options;

    public SepayWalletService(
        FinVietDbContext db,
        ISepayClient client,
        ISepayTokenProtector tokenProtector,
        ISepayLinkStateProtector stateProtector,
        IAiCategorizationService categorizationService,
        IMemoryCache cache,
        ILogger<SepayWalletService> logger,
        IOptions<SepayOptions> options)
    {
        _db = db;
        _client = client;
        _tokenProtector = tokenProtector;
        _stateProtector = stateProtector;
        _categorizationService = categorizationService;
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    // ── Authorization URL ───────────────────────────────────────────────────────

    public SepayAuthorizeUrlResponse CreateAuthorizeUrl(Guid customerId)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            throw new IntegrationUnavailableException(
                "SePay OAuth is not configured on this server.",
                "sepay_not_configured");
        }

        var lifetime = TimeSpan.FromMinutes(Math.Clamp(_options.LinkStateLifetimeMinutes, 1, 30));
        var state = _stateProtector.Protect(customerId, lifetime);

        var authorizeUri = Uri.TryCreate(_options.AuthorizeUrl, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(new Uri(EnsureTrailingSlash(_options.BaseUrl)), _options.AuthorizeUrl.TrimStart('/'));

        var query = string.Join('&',
            "response_type=code",
            $"client_id={Uri.EscapeDataString(_options.ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(_options.RedirectUri)}",
            $"scope={Uri.EscapeDataString(_options.Scopes)}",
            $"state={Uri.EscapeDataString(state)}");

        return new SepayAuthorizeUrlResponse
        {
            AuthorizeUrl = $"{authorizeUri.GetLeftPart(UriPartial.Path)}?{query}",
            State = state,
            ExpiresAt = DateTime.UtcNow.Add(lifetime)
        };
    }

    // ── Link (OAuth) ────────────────────────────────────────────────────────────

    public async Task<SepayLinkResult> LinkAccountAsync(
        Guid customerId,
        LinkSepayAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ValidationException("OAuth authorization code is required.");

        EnsureStateBelongsToCustomer(request.State, customerId);

        // 1. Exchange code for tokens (or reuse the token from the bank-account listing call).
        var token = await ExchangeCodeCachedAsync(customerId, request.Code.Trim(), cancellationToken);

        // 2. Fetch user profile (so we can store the SePay user id) and the bank accounts.
        var user = await _client.GetMeAsync(token.AccessToken, cancellationToken);
        var bankAccounts = await _client.GetBankAccountsAsync(token.AccessToken, cancellationToken);
        var activeAccounts = bankAccounts.Where(a => a.Active).ToList();
        if (activeAccounts.Count == 0)
            throw new ValidationException("No active bank accounts found on your SePay account.");

        // 3. Determine which bank account to link.
        SepayBankAccount account;
        if (request.BankAccountId.HasValue)
        {
            account = activeAccounts.FirstOrDefault(a => a.Id == request.BankAccountId.Value)
                ?? throw new NotFoundException($"SePay bank account {request.BankAccountId.Value} not found or inactive.");
        }
        else
        {
            account = activeAccounts[0]; // default to first
        }

        // 4. Pull the history *before* opening the database transaction — the fetch spans many
        //    HTTP round trips and must not hold a serializable transaction open for its duration.
        var remoteTransactions = await FetchAllTransactionsAsync(
            token.AccessToken, account.Id, null, cancellationToken);

        var now = DateTime.UtcNow;
        var accessTokenProtected = _tokenProtector.Protect(token.AccessToken);
        var refreshTokenProtected = string.IsNullOrWhiteSpace(token.RefreshToken)
            ? null
            : _tokenProtector.Protect(token.RefreshToken);

        var createdExpenseIds = new List<Guid>();
        var created = 0;
        Wallet wallet;
        SepayLink sepayLink;

        await using (var databaseTransaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken))
        {
            // Only manual (basic) wallets count against the limit — bank-linked wallets are
            // real accounts, not user-created quota, so they never block linking.
            var basicWalletCount = await _db.Wallets
                .CountAsync(w => w.CustomerId == customerId && !w.IsDeleted
                                 && w.WalletType == BasicWalletType, cancellationToken);

            // Check if this SePay bank account is already linked.
            var existingLink = await _db.SepayLinks
                .Include(l => l.Wallet)
                .FirstOrDefaultAsync(
                    l => l.SepayBankAccountId == account.Id
                         && l.Wallet.CustomerId == customerId
                         && !l.Wallet.IsDeleted,
                    cancellationToken);

            if (existingLink != null)
            {
                // Re-link: update tokens on the existing wallet.
                wallet = existingLink.Wallet;
                sepayLink = existingLink;
                wallet.Balance = account.Accumulated;
                wallet.UpdatedAt = now;
            }
            else
            {
                if (basicWalletCount >= MaximumWalletsPerCustomer)
                    throw new ValidationException("Adding this SePay bank account would exceed the 10-wallet limit.");

                var walletName = !string.IsNullOrWhiteSpace(account.Bank?.ShortName)
                    ? $"SePay - {account.Bank.ShortName}"
                    : $"SePay - {account.Label}";

                wallet = new Wallet
                {
                    WalletId = Guid.NewGuid(),
                    CustomerId = customerId,
                    WalletName = Truncate(walletName, 120)!,
                    WalletType = SepayWalletType,
                    Balance = account.Accumulated,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                sepayLink = new SepayLink
                {
                    WalletId = wallet.WalletId,
                    Wallet = wallet,
                    AuthMode = OAuthAuthMode,
                    CreatedAt = now
                };
                _db.Wallets.Add(wallet);
                _db.SepayLinks.Add(sepayLink);
            }

            sepayLink.AuthMode = OAuthAuthMode;
            sepayLink.SepayUserId = user.Id.ToString();
            sepayLink.SepayBankAccountId = account.Id;
            sepayLink.AccountNumber = account.AccountNumber;
            sepayLink.AccountHolderName = account.AccountHolderName;
            sepayLink.BankShortName = account.Bank?.ShortName ?? sepayLink.BankShortName;
            sepayLink.AccessTokenProtected = accessTokenProtected;
            sepayLink.RefreshTokenProtected = refreshTokenProtected ?? sepayLink.RefreshTokenProtected;
            sepayLink.AccessTokenExpiresAt = ResolveTokenExpiry(token, now);
            sepayLink.UpdatedAt = now;

            await _db.SaveChangesAsync(cancellationToken);

            // 5. Import the history that was fetched above.
            foreach (var remote in remoteTransactions)
            {
                var outcome = await UpsertNormalizedAsync(
                    remote.Id.ToString(), remote.AmountIn, remote.AmountOut,
                    remote.TransactionContent, remote.TransactionDate,
                    customerId, wallet.WalletId, now, cancellationToken);
                if (!outcome.Inserted)
                    continue;

                created++;
                if (outcome.IsExpense)
                    createdExpenseIds.Add(outcome.TransactionId);
            }

            sepayLink.LastSyncedAt = now;
            sepayLink.UpdatedAt = now;

            await _db.SaveChangesAsync(cancellationToken);
            await databaseTransaction.CommitAsync(cancellationToken);
        }

        await CategorizeAsync(customerId, createdExpenseIds, cancellationToken);
        await TryAutoRegisterWebhookAsync(customerId, wallet.WalletId, cancellationToken);

        return new SepayLinkResult
        {
            Wallets = [ToWalletResponse(wallet, sepayLink)],
            TransactionsSynced = created
        };
    }

    /// <summary>
    /// Registers the webhook right after linking so real-time delivery starts without a second
    /// call. Skipped when no public webhook URL is configured, and never allowed to fail the
    /// link — the wallet is already usable, it just falls back to manual sync.
    /// </summary>
    private async Task TryAutoRegisterWebhookAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookUrl) || string.IsNullOrWhiteSpace(_options.WebhookApiKey))
            return;

        try
        {
            await RegisterWebhookAsync(customerId, walletId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Automatic SePay webhook registration failed for wallet {WalletId}; " +
                "the wallet is linked and can still be synced manually.",
                walletId);
        }
    }

    // ── Link with a personal SePay User API token (static) ──────────────────────

    public async Task<SepayLinkResult> LinkWithTokenAsync(
        Guid customerId,
        LinkSepayTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiToken = request.ApiToken?.Trim();
        if (string.IsNullOrWhiteSpace(apiToken))
            throw new ValidationException("SePay API token is required.");

        // 1. Validate the token by pulling the transaction history.
        SepayUserApiListResponse response;
        try
        {
            response = await _client.GetUserApiTransactionsAsync(
                apiToken, limit: UserApiLimit, accountNumber: request.AccountNumber,
                cancellationToken: cancellationToken);
        }
        catch (ExternalServiceException ex) when (ex.Code == "sepay_unauthorized")
        {
            throw new ValidationException("The SePay API token is invalid or expired.");
        }

        var transactions = response.Transactions;
        // Derive bank account metadata from the first row that carries it.
        var sample = transactions.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.AccountNumber));
        var accountNumber = request.AccountNumber ?? sample?.AccountNumber;
        var bankBrand = sample?.BankBrandName;
        var latestBalance = ResolveStaticBalance(transactions);

        var now = DateTime.UtcNow;
        var apiTokenProtected = _tokenProtector.Protect(apiToken);

        var createdExpenseIds = new List<Guid>();
        var created = 0;
        Wallet wallet;
        SepayLink sepayLink;

        await using (var databaseTransaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken))
        {
            // Only manual (basic) wallets count against the limit — bank-linked wallets are
            // real accounts, not user-created quota, so they never block linking.
            var basicWalletCount = await _db.Wallets
                .CountAsync(w => w.CustomerId == customerId && !w.IsDeleted
                                 && w.WalletType == BasicWalletType, cancellationToken);

            // Re-link if a static SePay wallet for this account already exists.
            var existingLink = await _db.SepayLinks
                .Include(l => l.Wallet)
                .FirstOrDefaultAsync(
                    l => l.AuthMode == StaticAuthMode
                         && l.AccountNumber == accountNumber
                         && l.Wallet.CustomerId == customerId
                         && !l.Wallet.IsDeleted,
                    cancellationToken);

            if (existingLink != null)
            {
                wallet = existingLink.Wallet;
                sepayLink = existingLink;
                if (latestBalance.HasValue) wallet.Balance = latestBalance.Value;
                wallet.UpdatedAt = now;
            }
            else
            {
                if (basicWalletCount >= MaximumWalletsPerCustomer)
                    throw new ValidationException("Adding this SePay bank account would exceed the 10-wallet limit.");

                var walletName = !string.IsNullOrWhiteSpace(bankBrand)
                    ? $"SePay - {bankBrand}"
                    : "SePay - Ngân hàng";

                wallet = new Wallet
                {
                    WalletId = Guid.NewGuid(),
                    CustomerId = customerId,
                    WalletName = Truncate(walletName, 120)!,
                    WalletType = SepayWalletType,
                    Balance = latestBalance ?? 0m,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                sepayLink = new SepayLink
                {
                    WalletId = wallet.WalletId,
                    Wallet = wallet,
                    AuthMode = StaticAuthMode,
                    SepayBankAccountId = 0,
                    AccountNumber = accountNumber,
                    CreatedAt = now
                };
                _db.Wallets.Add(wallet);
                _db.SepayLinks.Add(sepayLink);
            }

            sepayLink.BankShortName = bankBrand ?? sepayLink.BankShortName;
            sepayLink.AccessTokenProtected = apiTokenProtected;
            sepayLink.RefreshTokenProtected = null;
            sepayLink.AccessTokenExpiresAt = null; // static tokens do not expire
            sepayLink.UpdatedAt = now;

            await _db.SaveChangesAsync(cancellationToken);

            // Import the transaction history.
            foreach (var t in transactions.Select(ToNormalized))
            {
                var outcome = await UpsertNormalizedAsync(
                    t.Id, t.AmountIn, t.AmountOut, t.Content, t.Date,
                    customerId, wallet.WalletId, now, cancellationToken);
                if (!outcome.Inserted)
                    continue;

                created++;
                if (outcome.IsExpense)
                    createdExpenseIds.Add(outcome.TransactionId);
            }

            sepayLink.LastSyncedAt = now;
            sepayLink.UpdatedAt = now;

            await _db.SaveChangesAsync(cancellationToken);
            await databaseTransaction.CommitAsync(cancellationToken);
        }

        await CategorizeAsync(customerId, createdExpenseIds, cancellationToken);

        return new SepayLinkResult
        {
            Wallets = [ToWalletResponse(wallet, sepayLink)],
            TransactionsSynced = created
        };
    }

    // ── Bank accounts (for account selection UI) ────────────────────────────────

    public async Task<IReadOnlyList<SepayBankAccountResponse>> GetBankAccountsAsync(
        Guid customerId,
        SepayBankAccountsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ValidationException("OAuth authorization code is required.");

        EnsureStateBelongsToCustomer(request.State, customerId);

        var token = await ExchangeCodeCachedAsync(customerId, request.Code.Trim(), cancellationToken);
        var accounts = await _client.GetBankAccountsAsync(token.AccessToken, cancellationToken);

        var linkedIds = await _db.SepayLinks
            .Where(l => l.Wallet.CustomerId == customerId && !l.Wallet.IsDeleted)
            .Select(l => l.SepayBankAccountId)
            .ToListAsync(cancellationToken);
        var linked = linkedIds.ToHashSet();

        return accounts
            .Where(a => a.Active)
            .Select(a => new SepayBankAccountResponse
            {
                Id = a.Id,
                Label = a.Label,
                AccountNumber = AccountNumberMask.Apply(a.AccountNumber) ?? string.Empty,
                AccountHolderName = a.AccountHolderName,
                Balance = a.Accumulated,
                BankShortName = a.Bank?.ShortName ?? string.Empty,
                BankCode = a.Bank?.Code ?? string.Empty,
                BankIconUrl = a.Bank?.IconUrl,
                AlreadyLinked = linked.Contains(a.Id)
            })
            .ToList();
    }

    // ── Link status ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SepayLinkStatusResponse>> GetLinksAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var links = await _db.SepayLinks
            .AsNoTracking()
            .Include(l => l.Wallet)
            .Where(l => l.Wallet.CustomerId == customerId && !l.Wallet.IsDeleted)
            .OrderBy(l => l.Wallet.WalletName)
            .ToListAsync(cancellationToken);

        return links
            .Select(link => new SepayLinkStatusResponse
            {
                WalletId = link.WalletId,
                WalletName = link.Wallet.WalletName,
                Balance = link.Wallet.Balance ?? 0m,
                AuthMode = link.AuthMode,
                SepayBankAccountId = IsStatic(link) ? null : link.SepayBankAccountId,
                BankShortName = link.BankShortName,
                AccountMask = AccountNumberMask.Apply(link.AccountNumber),
                AccountHolderName = link.AccountHolderName,
                LastSyncedAt = link.LastSyncedAt,
                RelinkRequired = IsRelinkRequired(link),
                WebhookId = link.SepayWebhookId,
                WebhookRegistered = link.SepayWebhookId.HasValue
            })
            .ToList();
    }

    /// <summary>
    /// An OAuth link is dead once its access token has expired and there is no refresh token left
    /// to renew it; a static link is dead only when its API token is gone.
    /// </summary>
    private static bool IsRelinkRequired(SepayLink link)
    {
        if (string.IsNullOrWhiteSpace(link.AccessTokenProtected))
            return true;
        if (IsStatic(link) || !string.IsNullOrWhiteSpace(link.RefreshTokenProtected))
            return false;

        return link.AccessTokenExpiresAt.HasValue && link.AccessTokenExpiresAt.Value <= DateTime.UtcNow;
    }

    // ── Sync ────────────────────────────────────────────────────────────────────

    public async Task<SepayWalletSyncResponse> SyncWalletAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken = default)
    {
        var link = await _db.SepayLinks
            .Include(l => l.Wallet)
            .FirstOrDefaultAsync(
                l => l.Wallet.CustomerId == customerId
                     && l.WalletId == walletId
                     && !l.Wallet.IsDeleted,
                cancellationToken);

        if (link is null)
            throw new NotFoundException("SePay-linked wallet not found.");

        try
        {
            var now = DateTime.UtcNow;

            // Determine from_date: if we've synced before, only pull recent transactions
            // (a few days of overlap catches delayed postings; dedup absorbs the overlap).
            string? fromDate = null;
            if (link.LastSyncedAt.HasValue)
            {
                var overlap = Math.Clamp(_options.SyncOverlapDays, 0, 90);
                fromDate = link.LastSyncedAt.Value.AddDays(-overlap).ToString("yyyy-MM-dd");
            }

            // Fetch transactions + latest balance for whichever auth mode this link uses.
            List<NormalizedTxn> normalized;
            decimal? latestBalance;

            if (IsStatic(link))
            {
                var apiToken = _tokenProtector.Unprotect(link.AccessTokenProtected!);
                var response = await _client.GetUserApiTransactionsAsync(
                    apiToken, limit: UserApiLimit, accountNumber: link.AccountNumber, sinceDate: fromDate,
                    cancellationToken: cancellationToken);
                normalized = response.Transactions.Select(ToNormalized).ToList();
                // The newest row's accumulated is the current running balance.
                latestBalance = ResolveStaticBalance(response.Transactions);
            }
            else
            {
                var accessToken = await GetAccessTokenAsync(link, cancellationToken);
                SepayBankAccount? accountInfo = null;
                try
                {
                    accountInfo = await _client.GetBankAccountAsync(accessToken, link.SepayBankAccountId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch SePay bank account {Id} balance.", link.SepayBankAccountId);
                }
                var oauthTxns = await FetchAllTransactionsAsync(
                    accessToken, link.SepayBankAccountId, fromDate, cancellationToken);
                normalized = oauthTxns.Select(t => new NormalizedTxn(
                    t.Id.ToString(), t.AmountIn, t.AmountOut, t.TransactionContent, t.TransactionDate)).ToList();
                latestBalance = accountInfo?.Accumulated;
            }

            var created = 0;
            var updated = 0;
            var createdExpenseIds = new List<Guid>();

            await using (var databaseTransaction = await _db.Database.BeginTransactionAsync(cancellationToken))
            {
                foreach (var t in normalized)
                {
                    var outcome = await UpsertNormalizedAsync(
                        t.Id, t.AmountIn, t.AmountOut, t.Content, t.Date,
                        customerId, walletId, now, cancellationToken);
                    if (outcome.Inserted)
                    {
                        created++;
                        if (outcome.IsExpense)
                            createdExpenseIds.Add(outcome.TransactionId);
                    }
                    else if (outcome.TransactionId != Guid.Empty)
                    {
                        updated++;
                    }
                }

                // Update wallet balance.
                link.Wallet.Balance = latestBalance ?? link.Wallet.Balance;
                link.Wallet.UpdatedAt = now;
                link.LastSyncedAt = now;
                link.UpdatedAt = now;

                await _db.SaveChangesAsync(cancellationToken);
                await databaseTransaction.CommitAsync(cancellationToken);
            }

            // AI categorization for new expenses (outside the DB transaction).
            await CategorizeAsync(customerId, createdExpenseIds, cancellationToken);

            return new SepayWalletSyncResponse
            {
                WalletId = walletId,
                Balance = link.Wallet.Balance ?? 0m,
                TransactionsCreated = created,
                TransactionsUpdated = updated,
                SyncedAt = now
            };
        }
        catch (Exception ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "SePay sync failed for wallet {WalletId}.", walletId);
            throw;
        }
    }

    public async Task<SepaySyncAllResponse> SyncAllWalletsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var walletIds = await _db.SepayLinks
            .AsNoTracking()
            .Where(l => l.Wallet.CustomerId == customerId && !l.Wallet.IsDeleted)
            .Select(l => l.WalletId)
            .ToListAsync(cancellationToken);

        var results = new List<SepayWalletSyncResponse>();
        var failures = new Dictionary<Guid, string>();

        // One bad link (expired authorization, bank outage) must not cost the customer the
        // sync of every other wallet, so failures are collected instead of thrown.
        foreach (var walletId in walletIds)
        {
            try
            {
                results.Add(await SyncWalletAsync(customerId, walletId, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures[walletId] = ex is BusinessRuleException or ExternalServiceException
                    ? ex.Message
                    : "Synchronization failed.";
            }
        }

        return new SepaySyncAllResponse
        {
            Wallets = results,
            TransactionsCreated = results.Sum(r => r.TransactionsCreated),
            TransactionsUpdated = results.Sum(r => r.TransactionsUpdated),
            Failures = failures
        };
    }

    // ── Webhook registration on SePay ───────────────────────────────────────────

    public async Task<SepayWebhookRegistrationResponse> RegisterWebhookAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken = default)
    {
        var link = await LoadWebhookCapableLinkAsync(customerId, walletId, cancellationToken);

        if (string.IsNullOrWhiteSpace(_options.WebhookApiKey))
        {
            throw new IntegrationUnavailableException(
                "SePay:WebhookApiKey is not configured, so the receiver would reject every delivery.",
                "sepay_webhook_disabled");
        }

        var webhookUrl = _options.WebhookUrl?.Trim();
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            throw new IntegrationUnavailableException(
                "SePay:WebhookUrl is not configured.",
                "sepay_webhook_url_missing");
        }

        // SePay only calls public HTTPS endpoints; catching this here gives a clear message
        // instead of an opaque validation error from their API.
        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
            || parsed.IsLoopback)
        {
            throw new IntegrationUnavailableException(
                "SePay:WebhookUrl must be a public http(s) URL — SePay cannot reach localhost.",
                "sepay_webhook_url_invalid");
        }

        var accessToken = await GetAccessTokenAsync(link, cancellationToken);

        // Adopt an existing webhook rather than stacking duplicates, so calling this twice (or
        // after a re-link) leaves exactly one registration behind.
        var existing = (await _client.GetWebhooksAsync(accessToken, cancellationToken))
            .FirstOrDefault(w => w.BankAccountId == link.SepayBankAccountId
                                 && string.Equals(w.WebhookUrl?.TrimEnd('/'), webhookUrl.TrimEnd('/'),
                                     StringComparison.OrdinalIgnoreCase));

        var webhookId = existing?.Id ?? await _client.CreateWebhookAsync(
            accessToken,
            new SepayWebhookCreateRequest
            {
                BankAccountId = link.SepayBankAccountId,
                Name = Truncate($"{_options.WebhookName} - {link.BankShortName ?? "Bank"}", 120)!,
                EventType = SepayWebhookConstants.EventTypeAll,
                AuthenType = SepayWebhookConstants.AuthenTypeApiKey,
                WebhookUrl = webhookUrl,
                ApiKey = _options.WebhookApiKey,
                RequestContentType = SepayWebhookConstants.ContentTypeJson,
                IsVerifyPayment = 0,
                Active = 1
            },
            cancellationToken);

        link.SepayWebhookId = webhookId;
        link.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "SePay webhook {WebhookId} {Action} for wallet {WalletId}.",
            webhookId, existing is null ? "created" : "adopted", walletId);

        return new SepayWebhookRegistrationResponse
        {
            WalletId = walletId,
            WebhookId = webhookId,
            WebhookUrl = webhookUrl,
            EventType = SepayWebhookConstants.EventTypeAll,
            AlreadyExisted = existing is not null
        };
    }

    public async Task<SepayWebhookRegistrationResponse> UnregisterWebhookAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken = default)
    {
        var link = await LoadWebhookCapableLinkAsync(customerId, walletId, cancellationToken);

        if (!link.SepayWebhookId.HasValue)
        {
            throw new NotFoundException("No SePay webhook is registered for this wallet.");
        }

        var webhookId = link.SepayWebhookId.Value;
        var accessToken = await GetAccessTokenAsync(link, cancellationToken);
        await _client.DeleteWebhookAsync(accessToken, webhookId, cancellationToken);

        link.SepayWebhookId = null;
        link.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return new SepayWebhookRegistrationResponse
        {
            WalletId = walletId,
            WebhookId = webhookId,
            WebhookUrl = _options.WebhookUrl,
            EventType = SepayWebhookConstants.EventTypeAll
        };
    }

    private async Task<SepayLink> LoadWebhookCapableLinkAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken)
    {
        var link = await _db.SepayLinks
            .Include(l => l.Wallet)
            .FirstOrDefaultAsync(
                l => l.Wallet.CustomerId == customerId
                     && l.WalletId == walletId
                     && !l.Wallet.IsDeleted,
                cancellationToken);

        if (link is null)
            throw new NotFoundException("SePay-linked wallet not found.");

        // The static User API token authenticates against /userapi only; webhook management
        // lives behind OAuth scopes it can never hold.
        if (IsStatic(link))
        {
            throw new BusinessRuleException(
                "Webhooks can only be managed on an OAuth-linked wallet. Re-link this wallet with SePay OAuth.",
                "sepay_webhook_requires_oauth");
        }

        return link;
    }

    // ── Unlink ──────────────────────────────────────────────────────────────────

    public async Task<SepayUnlinkResponse> UnlinkWalletAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken = default)
    {
        var link = await _db.SepayLinks
            .Include(l => l.Wallet)
            .FirstOrDefaultAsync(
                l => l.Wallet.CustomerId == customerId
                     && l.WalletId == walletId
                     && !l.Wallet.IsDeleted,
                cancellationToken);

        if (link is null)
            throw new NotFoundException("SePay-linked wallet not found.");

        var retained = await _db.Transactions
            .CountAsync(t => t.WalletId == walletId && t.EntryMethod == SepayEntryMethod, cancellationToken);

        // Best effort: drop the webhook we registered so SePay stops posting to an endpoint that
        // can no longer route the delivery. A failure here must not block the unlink — the user
        // asked to disconnect, and the receiver ignores unmatched accounts anyway.
        if (link.SepayWebhookId.HasValue && !IsStatic(link))
        {
            try
            {
                var accessToken = await GetAccessTokenAsync(link, cancellationToken);
                await _client.DeleteWebhookAsync(accessToken, link.SepayWebhookId.Value, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not delete SePay webhook {WebhookId} while unlinking wallet {WalletId}; " +
                    "remove it by hand in the SePay dashboard.",
                    link.SepayWebhookId.Value, walletId);
            }
        }

        // The wallet and its history stay; only the authorization goes away. Turning it back into
        // a basic wallet is what lifts the read-only rules on transfers and manual entries.
        link.Wallet.WalletType = BasicWalletType;
        link.Wallet.UpdatedAt = DateTime.UtcNow;
        _db.SepayLinks.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);

        return new SepayUnlinkResponse
        {
            WalletId = walletId,
            WalletType = BasicWalletType,
            TransactionsRetained = retained
        };
    }

    // ── Webhook ─────────────────────────────────────────────────────────────────

    public async Task<SepayWebhookResult> HandleWebhookAsync(
        string? apiKeyHeader,
        SepayWebhookRequest payload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookApiKey))
        {
            throw new IntegrationUnavailableException(
                "The SePay webhook is not enabled on this server.",
                "sepay_webhook_disabled");
        }

        if (!IsValidWebhookKey(apiKeyHeader))
            throw new UnauthorizedException("Invalid SePay webhook API key.");

        if (payload.Id <= 0)
            throw new ValidationException("Webhook payload is missing the transaction id.");

        var accountNumber = payload.AccountNumber?.Trim();
        var subAccount = payload.SubAccount?.Trim();
        if (string.IsNullOrWhiteSpace(accountNumber) && string.IsNullOrWhiteSpace(subAccount))
            throw new ValidationException("Webhook payload is missing the account number.");

        // Direction decides income vs expense, so guessing it would silently book money the wrong
        // way round; reject the delivery instead and let SePay retry.
        var isIncome = string.Equals(payload.TransferType, "in", StringComparison.OrdinalIgnoreCase);
        if (!isIncome && !string.Equals(payload.TransferType, "out", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Webhook payload transferType must be \"in\" or \"out\".");

        var matches = await _db.SepayLinks
            .Include(l => l.Wallet)
            .Where(l => !l.Wallet.IsDeleted
                        && l.AccountNumber != null
                        && (l.AccountNumber == accountNumber || l.AccountNumber == subAccount))
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            // Acknowledge anyway: retrying a delivery we can never route wastes SePay's queue.
            _logger.LogInformation(
                "SePay webhook for transaction {TransactionId} matched no linked wallet; ignored.", payload.Id);
            return new SepayWebhookResult { Outcome = "ignored" };
        }

        if (matches.Count > 1)
        {
            // Crediting the wrong customer is worse than dropping the event; the periodic sync
            // still picks the transaction up for whichever wallet legitimately owns it.
            _logger.LogWarning(
                "SePay webhook for transaction {TransactionId} matched {Count} wallets; ignored as ambiguous.",
                payload.Id, matches.Count);
            return new SepayWebhookResult { Outcome = "ignored" };
        }

        var link = matches[0];
        var customerId = link.Wallet.CustomerId
            ?? throw new BusinessRuleException("The linked wallet has no owner.", "sepay_wallet_orphaned");

        var amountIn = isIncome ? payload.TransferAmount : 0m;
        var amountOut = isIncome ? 0m : payload.TransferAmount;
        var content = !string.IsNullOrWhiteSpace(payload.Content) ? payload.Content : payload.Description;
        var now = DateTime.UtcNow;

        SepayUpsertOutcome outcome;
        await using (var databaseTransaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            outcome = await UpsertNormalizedAsync(
                payload.Id.ToString(), amountIn, amountOut, content, payload.TransactionDate,
                customerId, link.WalletId, now, cancellationToken);

            // SePay omits `accumulated` for gateways that do not report a running balance, and an
            // omitted field deserializes to 0 — so only a positive value is treated as authoritative.
            if (payload.Accumulated > 0m)
                link.Wallet.Balance = payload.Accumulated;
            link.Wallet.UpdatedAt = now;
            link.LastSyncedAt = now;
            link.UpdatedAt = now;

            await _db.SaveChangesAsync(cancellationToken);
            await databaseTransaction.CommitAsync(cancellationToken);
        }

        if (outcome.Inserted && outcome.IsExpense)
            await CategorizeAsync(customerId, [outcome.TransactionId], cancellationToken);

        return new SepayWebhookResult
        {
            Outcome = outcome.TransactionId == Guid.Empty ? "ignored" : outcome.Inserted ? "created" : "updated",
            WalletId = outcome.TransactionId == Guid.Empty ? null : link.WalletId
        };
    }

    private bool IsValidWebhookKey(string? apiKeyHeader)
    {
        if (string.IsNullOrWhiteSpace(apiKeyHeader))
            return false;

        // SePay sends `Authorization: Apikey <key>`; accept the bare key too for flexibility.
        var value = apiKeyHeader.Trim();
        const string prefix = "Apikey ";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            value = value[prefix.Length..].Trim();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(value),
            Encoding.UTF8.GetBytes(_options.WebhookApiKey));
    }

    // ── OAuth helpers ───────────────────────────────────────────────────────────

    private void EnsureStateBelongsToCustomer(string? state, Guid customerId)
    {
        if (string.IsNullOrWhiteSpace(state))
            return; // state is optional for clients still on the pre-CSRF flow

        Guid stateCustomerId;
        try
        {
            stateCustomerId = _stateProtector.UnprotectCustomerId(state.Trim());
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new BusinessRuleException(
                "The SePay link state is invalid or has expired. Start the authorization again.",
                "sepay_state_invalid");
        }

        if (stateCustomerId != customerId)
        {
            throw new BusinessRuleException(
                "The SePay link state does not belong to the authenticated customer.",
                "sepay_state_invalid");
        }
    }

    private async Task<SepayTokenResponse> ExchangeCodeCachedAsync(
        Guid customerId,
        string code,
        CancellationToken cancellationToken)
    {
        // Hashed so an authorization code never sits in memory as a cache key, and scoped to the
        // customer so one caller can never pick up a token minted for another.
        var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
        var cacheKey = $"sepay:code:{customerId:N}:{codeHash}";
        if (_cache.TryGetValue<SepayTokenResponse>(cacheKey, out var cached) && cached is not null)
            return cached;

        var token = await _client.ExchangeCodeAsync(code, cancellationToken);
        if (string.IsNullOrWhiteSpace(token.AccessToken))
            throw new ExternalServiceException("SePay did not return an access token.", "sepay_token_empty");

        _cache.Set(cacheKey, token, CodeExchangeCacheLifetime);
        return token;
    }

    /// <summary>
    /// Returns a usable access token, refreshing only when the stored one is expiring. The previous
    /// implementation refreshed on every sync, which burned a refresh-token rotation per call.
    /// </summary>
    private async Task<string> GetAccessTokenAsync(
        SepayLink link,
        CancellationToken cancellationToken)
    {
        var skew = TimeSpan.FromSeconds(Math.Clamp(_options.AccessTokenExpirySkewSeconds, 0, 3600));
        var stillValid = link.AccessTokenExpiresAt.HasValue
                         && link.AccessTokenExpiresAt.Value - skew > DateTime.UtcNow;

        if (stillValid && !string.IsNullOrWhiteSpace(link.AccessTokenProtected))
            return _tokenProtector.Unprotect(link.AccessTokenProtected);

        if (!string.IsNullOrWhiteSpace(link.RefreshTokenProtected))
        {
            try
            {
                var refreshToken = _tokenProtector.Unprotect(link.RefreshTokenProtected);
                var refreshed = await _client.RefreshTokenAsync(refreshToken, cancellationToken);
                if (!string.IsNullOrWhiteSpace(refreshed.AccessToken))
                {
                    var now = DateTime.UtcNow;
                    link.AccessTokenProtected = _tokenProtector.Protect(refreshed.AccessToken);
                    if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                        link.RefreshTokenProtected = _tokenProtector.Protect(refreshed.RefreshToken);
                    link.AccessTokenExpiresAt = ResolveTokenExpiry(refreshed, now);
                    link.UpdatedAt = now;
                    await _db.SaveChangesAsync(cancellationToken);
                    return refreshed.AccessToken;
                }
            }
            catch (ExternalServiceException ex) when (ex.Code == "sepay_unauthorized" || ex.Code == "sepay_validation_error")
            {
                _logger.LogWarning(ex, "SePay token refresh failed; refresh token may be expired.");
                throw new BusinessRuleException(
                    "SePay authorization has expired. Please re-link your bank account.",
                    "sepay_relink_required");
            }
        }

        // Fall back to the stored access token — links created before expiry tracking have no
        // recorded expiry, and the token may still be inside its window.
        if (!string.IsNullOrWhiteSpace(link.AccessTokenProtected))
            return _tokenProtector.Unprotect(link.AccessTokenProtected);

        throw new BusinessRuleException(
            "SePay authorization is missing. Link the bank account again.",
            "sepay_relink_required");
    }

    private static DateTime? ResolveTokenExpiry(SepayTokenResponse token, DateTime now)
        => token.ExpiresIn > 0 ? now.AddSeconds(token.ExpiresIn) : null;

    // ── Transaction fetch (all pages) ───────────────────────────────────────────

    private int UserApiLimit => Math.Clamp(_options.UserApiPageSize, 1, 5000);

    private async Task<List<SepayTransaction>> FetchAllTransactionsAsync(
        string accessToken,
        int bankAccountId,
        string? fromDate,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(_options.TransactionPageSize, 1, 100);
        var maxPages = Math.Clamp(_options.MaxTransactionPages, 1, 100);
        var result = new List<SepayTransaction>();

        for (var page = 1; page <= maxPages; page++)
        {
            var response = await _client.GetTransactionsAsync(
                accessToken, bankAccountId, page, limit, fromDate, null, cancellationToken);

            result.AddRange(response.Data);

            var pagination = response.Meta?.Pagination;
            if (pagination == null || page >= pagination.LastPage || response.Data.Count < limit)
                break;
        }

        return result;
    }

    // ── Transaction upsert ──────────────────────────────────────────────────────

    /// <summary>
    /// Upsert one SePay transaction (from the OAuth API, the static User API, or a webhook), keyed
    /// on external_id = "sepay:{id}". Amounts follow SePay's amount_in / amount_out convention.
    /// </summary>
    private async Task<SepayUpsertOutcome> UpsertNormalizedAsync(
        string? rawId,
        decimal amountIn,
        decimal amountOut,
        string? content,
        string? rawDate,
        Guid customerId,
        Guid walletId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawId))
            return new SepayUpsertOutcome(Guid.Empty, false, false);

        var isIncome = amountIn > 0;
        var isExpense = amountOut > 0;
        if (!isIncome && !isExpense) // skip zero rows
            return new SepayUpsertOutcome(Guid.Empty, false, false);

        var amount = isIncome ? amountIn : amountOut;
        var transactionType = isIncome ? "income" : "expense";
        var transactionDate = ParseVnDate(rawDate);
        var externalId = $"sepay:{rawId}";
        if (externalId.Length > 120) externalId = externalId[..120];
        var proposedId = Guid.NewGuid();

        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            INSERT INTO transactions (
                id, customer_id, wallet_id, amount, type, description, merchant,
                transaction_date, entry_method, external_id, created_at, updated_at)
            VALUES (
                @id, @customer_id, @wallet_id, @amount,
                CAST(@transaction_type AS transaction_type), @description, @merchant,
                @transaction_date, CAST(@entry_method AS entry_method), @external_id,
                @created_at, @updated_at)
            ON CONFLICT (external_id) WHERE external_id IS NOT NULL
            DO UPDATE SET
                wallet_id = EXCLUDED.wallet_id,
                amount = EXCLUDED.amount,
                type = EXCLUDED.type,
                description = EXCLUDED.description,
                transaction_date = EXCLUDED.transaction_date,
                entry_method = EXCLUDED.entry_method,
                updated_at = EXCLUDED.updated_at
            WHERE transactions.customer_id = EXCLUDED.customer_id
            RETURNING id, (xmax = 0) AS inserted;
            """;

        AddParameter(command, "id", proposedId, DbType.Guid);
        AddParameter(command, "customer_id", customerId, DbType.Guid);
        AddParameter(command, "wallet_id", walletId, DbType.Guid);
        AddParameter(command, "amount", amount, DbType.Decimal);
        AddParameter(command, "transaction_type", transactionType, DbType.String);
        AddParameter(command, "description", Truncate(content, 255), DbType.String);
        AddParameter(command, "merchant", (object?)null!, DbType.String); // SePay doesn't expose merchant
        AddParameter(command, "transaction_date", new DateTimeOffset(transactionDate), DbType.DateTimeOffset);
        AddParameter(command, "entry_method", SepayEntryMethod, DbType.String);
        AddParameter(command, "external_id", externalId, DbType.String);
        AddParameter(command, "created_at", new DateTimeOffset(now), DbType.DateTimeOffset);
        AddParameter(command, "updated_at", new DateTimeOffset(now), DbType.DateTimeOffset);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            // Conflict on external_id belonging to a different customer — skip silently.
            _logger.LogWarning("SePay transaction {ExternalId} skipped: belongs to another customer.", externalId);
            return new SepayUpsertOutcome(Guid.Empty, false, false);
        }

        return new SepayUpsertOutcome(
            reader.GetGuid(0),
            reader.GetBoolean(1),
            transactionType == "expense");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs AI categorization for newly created expenses. A categorization failure never rolls back
    /// an already-committed sync — the transactions simply stay uncategorized.
    /// </summary>
    private async Task CategorizeAsync(
        Guid customerId,
        IReadOnlyCollection<Guid> transactionIds,
        CancellationToken cancellationToken)
    {
        foreach (var transactionId in transactionIds)
        {
            try
            {
                await _categorizationService.CategorizeTransactionAsync(
                    customerId,
                    transactionId,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "AI categorization failed for SePay transaction {TransactionId}; sync committed.",
                    transactionId);
            }
        }
    }

    private static bool IsStatic(SepayLink link)
        => string.Equals(link.AuthMode, StaticAuthMode, StringComparison.OrdinalIgnoreCase);

    private static string EnsureTrailingSlash(string url)
        => string.IsNullOrWhiteSpace(url) ? "https://my.sepay.vn/" : url.EndsWith('/') ? url : $"{url}/";

    /// <summary>Normalized transaction shape shared by the OAuth and static User API paths.</summary>
    private sealed record NormalizedTxn(string? Id, decimal AmountIn, decimal AmountOut, string? Content, string? Date);

    private static NormalizedTxn ToNormalized(SepayUserApiTransaction t)
        => new(t.Id, ParseDecimal(t.AmountIn), ParseDecimal(t.AmountOut), t.TransactionContent, t.TransactionDate);

    /// <summary>
    /// The newest transaction's accumulated value is the current running balance. Rows share a
    /// timestamp often enough (batch postings) that the id breaks the tie — SePay ids increase
    /// monotonically, so the highest id within the newest timestamp is the latest state.
    /// </summary>
    private static decimal? ResolveStaticBalance(List<SepayUserApiTransaction> transactions)
    {
        var newest = transactions
            .Where(t => !string.IsNullOrWhiteSpace(t.Accumulated))
            .OrderByDescending(t => ParseVnDate(t.TransactionDate))
            .ThenByDescending(t => long.TryParse(t.Id, out var id) ? id : 0L)
            .FirstOrDefault();
        return newest is null ? null : ParseDecimal(newest.Accumulated);
    }

    /// <summary>SePay User API amounts arrive as strings, sometimes with thousands separators.</summary>
    private static decimal ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0m;
        var cleaned = raw.Replace(",", string.Empty).Trim();
        return decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0m;
    }

    /// <summary>SePay dates are VN-local (e.g. "2025-02-25 19:59:48"); treat as +07:00.</summary>
    private static DateTime ParseVnDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DateTime.UtcNow;

        var s = raw.Trim();
        // If already has timezone info, parse directly.
        if (s.EndsWith('Z') || s.Contains('+') || (s.Length > 10 && s[10..].Contains('-')))
        {
            if (DateTimeOffset.TryParse(s, out var dto))
                return dto.UtcDateTime;
        }

        // Treat as VN local time.
        var isoish = s.Replace(' ', 'T') + "+07:00";
        if (DateTimeOffset.TryParse(isoish, out var result))
            return result.UtcDateTime;

        return DateTime.UtcNow;
    }

    private static WalletResponse ToWalletResponse(Wallet wallet, SepayLink link)
        => new()
        {
            WalletId = wallet.WalletId,
            CustomerId = wallet.CustomerId ?? Guid.Empty,
            WalletName = wallet.WalletName,
            WalletType = SepayWalletType,
            Balance = wallet.Balance ?? 0m,
            SepayBankAccountId = IsStatic(link) ? null : link.SepayBankAccountId,
            InstitutionName = link.BankShortName,
            AccountMask = AccountNumberMask.Apply(link.AccountNumber),
            AuthMode = link.AuthMode,
            LastSyncedAt = link.LastSyncedAt
        };

    private static void AddParameter(DbCommand command, string name, object? value, DbType dbType)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = dbType;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed[..Math.Min(trimmed.Length, maxLength)];
    }

    private sealed record SepayUpsertOutcome(Guid TransactionId, bool Inserted, bool IsExpense);
}
