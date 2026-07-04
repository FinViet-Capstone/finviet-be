using System.Data;
using System.Data.Common;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.ExternalServices.SePay;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    private const int MaximumWalletsPerCustomer = 10;

    private readonly FinVietDbContext _db;
    private readonly ISepayClient _client;
    private readonly ISepayTokenProtector _tokenProtector;
    private readonly IAiCategorizationService _categorizationService;
    private readonly ILogger<SepayWalletService> _logger;
    private readonly SepayOptions _options;

    public SepayWalletService(
        FinVietDbContext db,
        ISepayClient client,
        ISepayTokenProtector tokenProtector,
        IAiCategorizationService categorizationService,
        ILogger<SepayWalletService> logger,
        IOptions<SepayOptions> options)
    {
        _db = db;
        _client = client;
        _tokenProtector = tokenProtector;
        _categorizationService = categorizationService;
        _logger = logger;
        _options = options.Value;
    }

    // ── Link ────────────────────────────────────────────────────────────────────

    public async Task<SepayLinkResult> LinkAccountAsync(
        Guid customerId,
        LinkSepayAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ValidationException("OAuth authorization code is required.");

        // 1. Exchange code for tokens.
        var token = await _client.ExchangeCodeAsync(request.Code, cancellationToken);
        if (string.IsNullOrWhiteSpace(token.AccessToken))
            throw new ExternalServiceException("SePay did not return an access token.", "sepay_token_empty");

        // 2. Fetch user profile (so we can store the SePay user id).
        var user = await _client.GetMeAsync(token.AccessToken, cancellationToken);

        // 3. Fetch bank accounts.
        var bankAccounts = await _client.GetBankAccountsAsync(token.AccessToken, cancellationToken);
        var activeAccounts = bankAccounts.Where(a => a.Active).ToList();
        if (activeAccounts.Count == 0)
            throw new ValidationException("No active bank accounts found on your SePay account.");

        // 4. Determine which bank account to link.
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

        // 5. Create wallet + SepayLink in a transaction.
        var now = DateTime.UtcNow;
        var accessTokenProtected = _tokenProtector.Protect(token.AccessToken);
        var refreshTokenProtected = string.IsNullOrWhiteSpace(token.RefreshToken)
            ? null
            : _tokenProtector.Protect(token.RefreshToken);

        await using var databaseTransaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

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

        Wallet wallet;
        SepayLink sepayLink;

        if (existingLink != null)
        {
            // Re-link: update tokens on existing wallet.
            wallet = existingLink.Wallet;
            sepayLink = existingLink;
            sepayLink.AccessTokenProtected = accessTokenProtected;
            sepayLink.RefreshTokenProtected = refreshTokenProtected ?? sepayLink.RefreshTokenProtected;
            sepayLink.SepayUserId = user.Id.ToString();
            sepayLink.UpdatedAt = now;
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
                SepayUserId = user.Id.ToString(),
                SepayBankAccountId = account.Id,
                AccountNumber = account.AccountNumber,
                AccountHolderName = account.AccountHolderName,
                BankShortName = account.Bank?.ShortName,
                AccessTokenProtected = accessTokenProtected,
                RefreshTokenProtected = refreshTokenProtected,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Wallets.Add(wallet);
            _db.SepayLinks.Add(sepayLink);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // 6. Perform initial transaction sync.
        var syncCount = await SyncTransactionsInternalAsync(
            customerId, wallet.WalletId, sepayLink, token.AccessToken, now, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        return new SepayLinkResult
        {
            Wallets = [ToWalletResponse(wallet, sepayLink)],
            TransactionsSynced = syncCount
        };
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
                apiToken, limit: 5000, accountNumber: request.AccountNumber,
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
        var latestBalance = transactions.Count > 0 ? ResolveStaticBalance(transactions) : null;

        var now = DateTime.UtcNow;
        var apiTokenProtected = _tokenProtector.Protect(apiToken);

        await using var databaseTransaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        // Only manual (basic) wallets count against the limit — bank-linked wallets are
        // real accounts, not user-created quota, so they never block linking.
        var basicWalletCount = await _db.Wallets
            .CountAsync(w => w.CustomerId == customerId && !w.IsDeleted
                             && w.WalletType == BasicWalletType, cancellationToken);

        // Re-link if a static SePay wallet for this account already exists.
        var existingLink = await _db.SepayLinks
            .Include(l => l.Wallet)
            .FirstOrDefaultAsync(
                l => l.AuthMode == "static"
                     && l.AccountNumber == accountNumber
                     && l.Wallet.CustomerId == customerId
                     && !l.Wallet.IsDeleted,
                cancellationToken);

        Wallet wallet;
        SepayLink sepayLink;

        if (existingLink != null)
        {
            wallet = existingLink.Wallet;
            sepayLink = existingLink;
            sepayLink.AccessTokenProtected = apiTokenProtected;
            sepayLink.BankShortName = bankBrand ?? sepayLink.BankShortName;
            sepayLink.UpdatedAt = now;
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
                AuthMode = "static",
                SepayBankAccountId = 0,
                AccountNumber = accountNumber,
                BankShortName = bankBrand,
                AccessTokenProtected = apiTokenProtected,
                RefreshTokenProtected = null,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Wallets.Add(wallet);
            _db.SepayLinks.Add(sepayLink);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Import the transaction history.
        var created = 0;
        foreach (var t in transactions.Select(ToNormalized))
        {
            var outcome = await UpsertNormalizedAsync(
                t.Id, t.AmountIn, t.AmountOut, t.Content, t.Date,
                customerId, wallet.WalletId, now, cancellationToken);
            if (outcome.Inserted) created++;
        }

        sepayLink.LastSyncedAt = now;
        sepayLink.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        return new SepayLinkResult
        {
            Wallets = [ToWalletResponse(wallet, sepayLink)],
            TransactionsSynced = created
        };
    }

    // ── Bank accounts (for account selection UI) ────────────────────────────────

    public async Task<IReadOnlyList<SepayBankAccountResponse>> GetBankAccountsAsync(
        Guid customerId,
        string code,
        CancellationToken cancellationToken = default)
    {
        // Exchange code first to get a temporary access token.
        var token = await _client.ExchangeCodeAsync(code, cancellationToken);
        if (string.IsNullOrWhiteSpace(token.AccessToken))
            throw new ExternalServiceException("SePay did not return an access token.", "sepay_token_empty");

        var accounts = await _client.GetBankAccountsAsync(token.AccessToken, cancellationToken);
        return accounts
            .Where(a => a.Active)
            .Select(a => new SepayBankAccountResponse
            {
                Id = a.Id,
                Label = a.Label,
                AccountNumber = a.AccountNumber,
                AccountHolderName = a.AccountHolderName,
                Balance = a.Accumulated,
                BankShortName = a.Bank?.ShortName ?? string.Empty,
                BankCode = a.Bank?.Code ?? string.Empty,
                BankIconUrl = a.Bank?.IconUrl
            })
            .ToList();
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
            // (3-day overlap to catch delayed postings; dedup handles the overlap).
            string? fromDate = null;
            if (link.LastSyncedAt.HasValue)
                fromDate = link.LastSyncedAt.Value.AddDays(-3).ToString("yyyy-MM-dd");

            // Fetch transactions + latest balance for whichever auth mode this link uses.
            List<NormalizedTxn> normalized;
            decimal? latestBalance;

            if (IsStatic(link))
            {
                var apiToken = _tokenProtector.Unprotect(link.AccessTokenProtected!);
                var response = await _client.GetUserApiTransactionsAsync(
                    apiToken, limit: 5000, accountNumber: link.AccountNumber, sinceDate: fromDate,
                    cancellationToken: cancellationToken);
                normalized = response.Transactions.Select(ToNormalized).ToList();
                // The newest row's accumulated is the current running balance.
                latestBalance = normalized.Count > 0 ? ResolveStaticBalance(response.Transactions) : null;
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
            foreach (var transactionId in createdExpenseIds)
            {
                try
                {
                    await _categorizationService.CategorizeTransactionAsync(transactionId, cancellationToken);
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

    // ── Internal sync (used by both link and manual sync) ───────────────────────

    private async Task<int> SyncTransactionsInternalAsync(
        Guid customerId,
        Guid walletId,
        SepayLink link,
        string accessToken,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var transactions = await FetchAllTransactionsAsync(
            accessToken, link.SepayBankAccountId, null, cancellationToken);

        var created = 0;
        foreach (var remote in transactions)
        {
            var outcome = await UpsertSepayTransactionAsync(
                remote, customerId, walletId, now, cancellationToken);
            if (outcome.Inserted)
                created++;
        }

        link.LastSyncedAt = now;
        link.UpdatedAt = now;
        return created;
    }

    // ── Token refresh ───────────────────────────────────────────────────────────

    private async Task<string> GetAccessTokenAsync(
        SepayLink link,
        CancellationToken cancellationToken)
    {
        // Try refresh token first (access token has 1h lifetime).
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

        // Fall back to stored access token (may still be valid within 1h window).
        if (!string.IsNullOrWhiteSpace(link.AccessTokenProtected))
            return _tokenProtector.Unprotect(link.AccessTokenProtected);

        throw new BusinessRuleException(
            "SePay authorization is missing. Link the bank account again.",
            "sepay_relink_required");
    }

    // ── Transaction fetch (all pages) ───────────────────────────────────────────

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

    private Task<SepayUpsertOutcome> UpsertSepayTransactionAsync(
        SepayTransaction remote,
        Guid customerId,
        Guid walletId,
        DateTime now,
        CancellationToken cancellationToken)
        => UpsertNormalizedAsync(
            remote.Id.ToString(),
            remote.AmountIn,
            remote.AmountOut,
            remote.TransactionContent,
            remote.TransactionDate,
            customerId,
            walletId,
            now,
            cancellationToken);

    /// <summary>
    /// Upsert one SePay transaction (from either the OAuth or static User API), keyed on
    /// external_id = "sepay:{id}". Amounts follow SePay's amount_in / amount_out convention.
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

    private static bool IsStatic(SepayLink link)
        => string.Equals(link.AuthMode, "static", StringComparison.OrdinalIgnoreCase);

    /// <summary>Normalized transaction shape shared by the OAuth and static User API paths.</summary>
    private sealed record NormalizedTxn(string? Id, decimal AmountIn, decimal AmountOut, string? Content, string? Date);

    private static NormalizedTxn ToNormalized(SepayUserApiTransaction t)
        => new(t.Id, ParseDecimal(t.AmountIn), ParseDecimal(t.AmountOut), t.TransactionContent, t.TransactionDate);

    /// <summary>The newest transaction's accumulated value is the current running balance.</summary>
    private static decimal? ResolveStaticBalance(List<SepayUserApiTransaction> transactions)
    {
        var newest = transactions
            .Where(t => !string.IsNullOrWhiteSpace(t.Accumulated))
            .OrderByDescending(t => ParseVnDate(t.TransactionDate))
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
            InstitutionName = link.BankShortName,
            AccountMask = link.AccountNumber,
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
