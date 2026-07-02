using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.ExternalServices.Finverse;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotFoundException = FinViet.Application.Common.Exceptions.NotFoundException;
using ValidationException = FinViet.Application.Exceptions.ValidationException;

namespace FinViet.Infrastructure.Services;

internal sealed class FinverseWalletService : IFinverseWalletService
{
    private const string FinverseWalletType = "finverse_linked";
    private const string FinverseEntryMethod = "finverse_sync";
    private const int MaximumWalletsPerCustomer = 10;
    private const int DataRetrievalMaxAttempts = 20;
    private static readonly TimeSpan DataRetrievalPollDelay = TimeSpan.FromSeconds(3);
    private static readonly HashSet<string> DataRetrievalDoneStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "DATA_RETRIEVAL_COMPLETE",
        "DATA_RETRIEVAL_PARTIALLY_SUCCESSFUL"
    };
    private static readonly TimeZoneInfo BusinessTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "Asia/Ho_Chi_Minh",
        TimeSpan.FromHours(7),
        "Asia/Ho_Chi_Minh",
        "Asia/Ho_Chi_Minh");

    private readonly FinVietDbContext _db;
    private readonly IFinverseClient _client;
    private readonly IFinverseTokenProtector _tokenProtector;
    private readonly IFinverseLinkStateProtector _stateProtector;
    private readonly IAiCategorizationService _categorizationService;
    private readonly ILogger<FinverseWalletService> _logger;
    private readonly FinverseOptions _options;

    public FinverseWalletService(
        FinVietDbContext db,
        IFinverseClient client,
        IFinverseTokenProtector tokenProtector,
        IFinverseLinkStateProtector stateProtector,
        IAiCategorizationService categorizationService,
        ILogger<FinverseWalletService> logger,
        IOptions<FinverseOptions> options)
    {
        _db = db;
        _client = client;
        _tokenProtector = tokenProtector;
        _stateProtector = stateProtector;
        _categorizationService = categorizationService;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<FinverseLinkTokenResponse> CreateLinkTokenAsync(
        Guid customerId,
        CreateFinverseLinkRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await _db.Customers.AnyAsync(c => c.CustomerId == customerId, cancellationToken))
            throw new NotFoundException("Customer not found.");

        var now = DateTime.UtcNow;
        var configuredLifetime = TimeSpan.FromMinutes(Math.Clamp(_options.LinkStateLifetimeMinutes, 1, 30));
        var state = _stateProtector.Protect(customerId, configuredLifetime);
        var apiResponse = await _client.CreateLinkTokenAsync(customerId, state, request, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiResponse.LinkUrl))
            throw new ExternalServiceException("Finverse did not return a link URL.", "finverse_link_url_missing");

        var finverseLifetime = TimeSpan.FromSeconds(apiResponse.ExpiresIn > 0 ? apiResponse.ExpiresIn : 300);
        var effectiveLifetime = finverseLifetime <= configuredLifetime ? finverseLifetime : configuredLifetime;
        return new FinverseLinkTokenResponse
        {
            LinkUrl = apiResponse.LinkUrl,
            State = state,
            ExpiresAt = now.Add(effectiveLifetime)
        };
    }

    public Task<FinverseLinkResult> CompleteLinkAsync(
        Guid customerId,
        CompleteFinverseLinkRequest request,
        CancellationToken cancellationToken = default)
        => CompleteLinkCoreAsync(customerId, request, cancellationToken);

    public Task<FinverseLinkResult> CompleteLinkCallbackAsync(
        CompleteFinverseLinkRequest request,
        CancellationToken cancellationToken = default)
        => CompleteLinkCoreAsync(null, request, cancellationToken);

    private async Task<FinverseLinkResult> CompleteLinkCoreAsync(
        Guid? expectedCustomerId,
        CompleteFinverseLinkRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.State))
            throw new ValidationException("Finverse code and state are required.");

        Guid customerId;
        try
        {
            customerId = _stateProtector.UnprotectCustomerId(request.State.Trim());
        }
        catch (CryptographicException)
        {
            throw new BusinessRuleException(
                "Finverse link state is invalid or expired.",
                "finverse_state_invalid");
        }

        if (expectedCustomerId.HasValue && expectedCustomerId.Value != customerId)
        {
            throw new BusinessRuleException(
                "Finverse link state does not belong to the authenticated customer.",
                "finverse_state_invalid");
        }

        if (!await _db.Customers.AnyAsync(c => c.CustomerId == customerId, cancellationToken))
            throw new NotFoundException("Customer not found.");

        var token = await _client.ExchangeCodeAsync(request.Code.Trim(), cancellationToken);
        if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.LoginIdentityId))
        {
            throw new ExternalServiceException(
                "Finverse did not return the complete Login Identity credentials.",
                "finverse_identity_token_missing");
        }

        // Finverse retrieves bank data asynchronously after linking; poll login_identity until the
        // retrieval finishes before reading accounts/transactions, otherwise the first read can be
        // empty/partial on slower banks (Finverse SDK polls ~20× every ~3s). Report §2.4 step 5.
        await WaitForDataRetrievalAsync(token.AccessToken, cancellationToken);

        var accountsResponse = await _client.GetAccountsAsync(token.AccessToken, cancellationToken);
        var accounts = accountsResponse.Accounts
            .Where(a => !a.IsParent && !a.IsExcluded && !a.IsClosed && !string.IsNullOrWhiteSpace(a.AccountId))
            .GroupBy(a => a.AccountId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        if (accounts.Count == 0)
            throw new BusinessRuleException("Finverse did not return any usable accounts.", "finverse_accounts_empty");

        var now = DateTime.UtcNow;
        await using var databaseTransaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var existingLinks = await _db.FinverseLinks
            .Include(link => link.Wallet)
            .Where(link => link.Wallet.CustomerId == customerId)
            .ToListAsync(cancellationToken);
        var linksByAccount = existingLinks
            .Where(link => !string.IsNullOrWhiteSpace(link.FinverseAccountId))
            .GroupBy(link => link.FinverseAccountId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var activeWalletCount = await _db.Wallets.CountAsync(
            wallet => wallet.CustomerId == customerId && !wallet.IsDeleted,
            cancellationToken);
        var usedNames = await _db.Wallets
            .Where(wallet => wallet.CustomerId == customerId && !wallet.IsDeleted)
            .Select(wallet => wallet.WalletName)
            .ToListAsync(cancellationToken);
        var names = new HashSet<string>(usedNames, StringComparer.OrdinalIgnoreCase);
        var accessTokenProtected = _tokenProtector.Protect(token.AccessToken);
        var refreshTokenProtected = string.IsNullOrWhiteSpace(token.RefreshToken)
            ? null
            : _tokenProtector.Protect(token.RefreshToken);

        var resultWallets = new List<WalletResponse>();
        foreach (var account in accounts)
        {
            if (!linksByAccount.TryGetValue(account.AccountId, out var link))
            {
                if (activeWalletCount >= MaximumWalletsPerCustomer)
                    throw new ValidationException("Linking these Finverse accounts would exceed the 10-wallet limit.");

                var wallet = new Wallet
                {
                    WalletId = Guid.NewGuid(),
                    CustomerId = customerId,
                    WalletName = BuildUniqueWalletName(account, names),
                    WalletType = FinverseWalletType,
                    Balance = account.Balance?.Value ?? 0m,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                link = new FinverseLink
                {
                    WalletId = wallet.WalletId,
                    Wallet = wallet,
                    CreatedAt = now
                };
                _db.Wallets.Add(wallet);
                _db.FinverseLinks.Add(link);
                activeWalletCount++;
            }
            else
            {
                if (link.Wallet.IsDeleted)
                {
                    if (activeWalletCount >= MaximumWalletsPerCustomer)
                        throw new ValidationException("Restoring this Finverse wallet would exceed the 10-wallet limit.");

                    activeWalletCount++;
                    names.Add(link.Wallet.WalletName);
                }

                link.Wallet.IsDeleted = false;
                link.Wallet.WalletType = FinverseWalletType;
                link.Wallet.Balance = account.Balance?.Value ?? link.Wallet.Balance;
                link.Wallet.UpdatedAt = now;
            }

            link.LoginIdentityId = token.LoginIdentityId;
            link.AccessTokenProtected = accessTokenProtected;
            link.RefreshTokenProtected = refreshTokenProtected ?? link.RefreshTokenProtected;
            link.FinverseAccountId = account.AccountId;
            link.InstitutionName = accountsResponse.Institution?.InstitutionName
                ?? accountsResponse.Institution?.Name;
            link.AccountMask = account.AccountNumberMasked;
            link.UpdatedAt = now;

            resultWallets.Add(ToWalletResponse(link.Wallet, link));
        }

        await _db.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        return new FinverseLinkResult
        {
            LoginIdentityId = token.LoginIdentityId,
            Wallets = resultWallets
        };
    }

    public async Task<FinverseWalletSyncResponse> SyncWalletAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken = default)
    {
        var link = await _db.FinverseLinks
            .Include(item => item.Wallet)
            .FirstOrDefaultAsync(
                item => item.Wallet.CustomerId == customerId
                        && item.WalletId == walletId
                        && !item.Wallet.IsDeleted,
                cancellationToken);
        if (link is null)
            throw new NotFoundException("Finverse-linked wallet not found.");

        if (string.IsNullOrWhiteSpace(link.FinverseAccountId))
        {
            throw new BusinessRuleException(
                "The Finverse wallet is missing its account identifier. Link it again.",
                "finverse_relink_required");
        }

        try
        {
            var accessToken = await GetAccessTokenAsync(link, customerId, cancellationToken);
            var accountsResponse = await _client.GetAccountsAsync(accessToken, cancellationToken);
            var account = accountsResponse.Accounts.FirstOrDefault(
                    item => item.AccountId == link.FinverseAccountId)
                ?? throw new ExternalServiceException(
                    "The linked Finverse account is no longer available.",
                    "finverse_account_missing");

            var remoteTransactions = await FetchAllTransactionsAsync(
                accessToken,
                link.FinverseAccountId,
                cancellationToken);
            if (remoteTransactions.Any(item =>
                    !item.IsPending
                    && !string.IsNullOrWhiteSpace(item.TransactionId)
                    && item.PostedDate == default))
            {
                throw new ExternalServiceException(
                    "Finverse returned a transaction without posted_date.",
                    "finverse_transaction_date_missing");
            }

            var pendingSkipped = remoteTransactions.Count(item => item.IsPending);
            var postedTransactions = remoteTransactions
                .Where(item => !item.IsPending && !string.IsNullOrWhiteSpace(item.TransactionId))
                .GroupBy(item => item.TransactionId, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
                .ToList();

            var created = 0;
            var updated = 0;
            var createdExpenseIds = new List<Guid>();
            var now = DateTime.UtcNow;
            await using (var databaseTransaction = await _db.Database.BeginTransactionAsync(cancellationToken))
            {
                foreach (var remote in postedTransactions)
                {
                    var outcome = await UpsertFinverseTransactionAsync(
                        remote,
                        customerId,
                        walletId,
                        now,
                        cancellationToken);
                    if (outcome.Inserted)
                    {
                        created++;
                        if (outcome.IsExpense)
                            createdExpenseIds.Add(outcome.TransactionId);
                    }
                    else
                    {
                        updated++;
                    }
                }

                link.Wallet.Balance = account.Balance?.Value ?? link.Wallet.Balance;
                link.Wallet.UpdatedAt = now;
                link.InstitutionName = accountsResponse.Institution?.InstitutionName
                    ?? accountsResponse.Institution?.Name
                    ?? link.InstitutionName;
                link.AccountMask = account.AccountNumberMasked ?? link.AccountMask;
                link.LastSyncedAt = now;
                link.UpdatedAt = now;

                await _db.SaveChangesAsync(cancellationToken);
                await databaseTransaction.CommitAsync(cancellationToken);
            }

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
                    _logger.LogWarning(
                        ex,
                        "AI categorization failed for Finverse transaction {TransactionId}; synchronization remains committed.",
                        transactionId);
                }
            }

            return new FinverseWalletSyncResponse
            {
                WalletId = walletId,
                Balance = link.Wallet.Balance ?? 0m,
                TransactionsCreated = created,
                TransactionsUpdated = updated,
                PendingTransactionsSkipped = pendingSkipped,
                SyncedAt = now
            };
        }
        catch (Exception ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Finverse synchronization failed for wallet {WalletId}.", walletId);
            throw;
        }
    }

    // Polls GET /login_identity until Finverse finishes retrieving the linked bank's data. Terminal
    // "done" statuses (complete / partially successful) or an unreadable status → proceed; an ERROR
    // status → fail the link; still in progress after the budget → proceed with whatever is ready.
    private async Task WaitForDataRetrievalAsync(string loginIdentityToken, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < DataRetrievalMaxAttempts; attempt++)
        {
            string? status;
            try
            {
                var identity = await _client.GetLoginIdentityAsync(loginIdentityToken, cancellationToken);
                status = identity.EffectiveStatus;
            }
            catch (ExternalServiceException ex)
            {
                // Non-fatal: if the status endpoint is unavailable, fall through and read the data.
                _logger.LogWarning(ex, "Finverse login_identity status poll failed; proceeding to read data.");
                return;
            }

            if (string.IsNullOrWhiteSpace(status) || DataRetrievalDoneStatuses.Contains(status))
                return;

            if (status.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                || status.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ExternalServiceException(
                    "Finverse could not retrieve the bank data for this login. Please link again.",
                    "finverse_data_retrieval_failed");
            }

            if (attempt < DataRetrievalMaxAttempts - 1)
                await Task.Delay(DataRetrievalPollDelay, cancellationToken);
        }

        _logger.LogWarning("Finverse data retrieval still in progress after polling; proceeding with available data.");
    }

    private async Task<string> GetAccessTokenAsync(
        FinverseLink link,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(link.RefreshTokenProtected))
        {
            var refreshToken = _tokenProtector.Unprotect(link.RefreshTokenProtected);
            var refreshed = await _client.RefreshLoginIdentityTokenAsync(refreshToken, cancellationToken);
            if (string.IsNullOrWhiteSpace(refreshed.AccessToken))
            {
                throw new ExternalServiceException(
                    "Finverse did not return a refreshed token.",
                    "finverse_refresh_failed");
            }

            var now = DateTime.UtcNow;
            var accessTokenProtected = _tokenProtector.Protect(refreshed.AccessToken);
            var refreshTokenProtected = string.IsNullOrWhiteSpace(refreshed.RefreshToken)
                ? link.RefreshTokenProtected
                : _tokenProtector.Protect(refreshed.RefreshToken);
            var siblingLinks = await _db.FinverseLinks
                .Include(item => item.Wallet)
                .Where(item => item.LoginIdentityId == link.LoginIdentityId
                               && item.Wallet.CustomerId == customerId)
                .ToListAsync(cancellationToken);
            foreach (var sibling in siblingLinks)
            {
                sibling.AccessTokenProtected = accessTokenProtected;
                sibling.RefreshTokenProtected = refreshTokenProtected;
                sibling.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return refreshed.AccessToken;
        }

        if (!string.IsNullOrWhiteSpace(link.AccessTokenProtected))
            return _tokenProtector.Unprotect(link.AccessTokenProtected);

        throw new BusinessRuleException(
            "Finverse authorization is missing. Link the bank account again.",
            "finverse_relink_required");
    }

    private async Task<FinverseUpsertOutcome> UpsertFinverseTransactionAsync(
        FinverseTransaction remote,
        Guid customerId,
        Guid walletId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var amount = Math.Abs(remote.Amount.Value);
        var transactionType = remote.Amount.Value >= 0 ? "income" : "expense";
        var transactionDate = ToBusinessDateUtc(remote.PostedDate);
        var externalId = ExternalId(remote.TransactionId);
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
                merchant = EXCLUDED.merchant,
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
        AddParameter(command, "description", Truncate(remote.Description, 255), DbType.String);
        AddParameter(command, "merchant", Truncate(remote.TransactionDetails?.CounterpartyName, 255), DbType.String);
        AddParameter(command, "transaction_date", new DateTimeOffset(transactionDate), DbType.DateTimeOffset);
        AddParameter(command, "entry_method", FinverseEntryMethod, DbType.String);
        AddParameter(command, "external_id", externalId, DbType.String);
        AddParameter(command, "created_at", new DateTimeOffset(now), DbType.DateTimeOffset);
        AddParameter(command, "updated_at", new DateTimeOffset(now), DbType.DateTimeOffset);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new BusinessRuleException(
                "The Finverse transaction identifier belongs to another customer.",
                "finverse_external_id_conflict");
        }

        return new FinverseUpsertOutcome(
            reader.GetGuid(0),
            reader.GetBoolean(1),
            transactionType == "expense");
    }

    private async Task<List<FinverseTransaction>> FetchAllTransactionsAsync(
        string accessToken,
        string accountId,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(_options.TransactionPageSize, 1, 1000);
        var maxPages = Math.Clamp(_options.MaxTransactionPages, 1, 100);
        var result = new List<FinverseTransaction>();

        for (var page = 0; page < maxPages; page++)
        {
            var offset = page * limit;
            var response = await _client.GetTransactionsAsync(
                accessToken,
                accountId,
                offset,
                limit,
                cancellationToken);
            result.AddRange(response.Transactions);

            if (response.Transactions.Count < limit || result.Count >= response.TotalTransactions)
                return result;
        }

        throw new ExternalServiceException(
            "Finverse transaction history exceeded the configured pagination limit.",
            "finverse_page_limit");
    }

    private static string BuildUniqueWalletName(FinverseAccount account, HashSet<string> usedNames)
    {
        var baseName = string.IsNullOrWhiteSpace(account.AccountNickname)
            ? account.AccountName
            : account.AccountNickname;
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Finverse account";

        baseName = Truncate(baseName.Trim(), 120)!;
        var candidate = baseName;
        if (usedNames.Add(candidate))
            return candidate;

        var suffix = string.IsNullOrWhiteSpace(account.AccountNumberMasked)
            ? account.AccountId[^Math.Min(6, account.AccountId.Length)..]
            : account.AccountNumberMasked;
        candidate = AppendWalletNameSuffix(baseName, suffix);
        for (var index = 2; !usedNames.Add(candidate); index++)
            candidate = AppendWalletNameSuffix(baseName, $"{suffix}-{index}");

        return candidate;
    }

    private static string ExternalId(string transactionId)
    {
        var externalId = $"finverse:{transactionId}";
        if (externalId.Length > 120)
        {
            throw new ExternalServiceException(
                "Finverse returned a transaction identifier longer than 120 characters.",
                "finverse_transaction_id_invalid");
        }

        return externalId;
    }

    private static DateTime ToBusinessDateUtc(DateOnly date)
    {
        var localMidnight = DateTime.SpecifyKind(
            date.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, BusinessTimeZone);
    }

    private static void AddParameter(DbCommand command, string name, object? value, DbType dbType)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = dbType;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string AppendWalletNameSuffix(string baseName, string suffix)
    {
        var marker = $" ({suffix})";
        var prefixLength = Math.Max(1, 120 - marker.Length);
        return $"{baseName[..Math.Min(baseName.Length, prefixLength)]}{marker}";
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed[..Math.Min(trimmed.Length, maxLength)];
    }

    private static WalletResponse ToWalletResponse(Wallet wallet, FinverseLink link)
        => new()
        {
            WalletId = wallet.WalletId,
            CustomerId = wallet.CustomerId ?? Guid.Empty,
            WalletName = wallet.WalletName,
            WalletType = FinverseWalletType,
            Balance = wallet.Balance ?? 0m,
            FinverseAccountId = link.FinverseAccountId,
            InstitutionName = link.InstitutionName,
            AccountMask = link.AccountMask,
            LastSyncedAt = link.LastSyncedAt
        };

    private sealed record FinverseUpsertOutcome(Guid TransactionId, bool Inserted, bool IsExpense);
}
