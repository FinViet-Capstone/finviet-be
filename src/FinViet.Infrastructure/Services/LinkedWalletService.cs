using System.Globalization;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.LinkedWallets;
using FinViet.Application.Interfaces;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.ExternalServices.Sepay;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FinViet.Infrastructure.Services;

/// <summary>
/// SePay-backed linked-wallet flow (per-user tokens). Each customer supplies their own SePay API
/// token at exchange; it is validated against SePay, held briefly behind an opaque accessToken
/// handle, then encrypted and stored on wallet_links when a wallet is linked. accounts/sync use
/// the per-user token — never a shared one. Sync pulls transactions via since_id polling and dedups
/// on transactions.external_id.
/// </summary>
public class LinkedWalletService : ILinkedWalletService
{
    private const string SepayLinkedWalletType = "sepay_linked";
    private const int SyncPageSize = 100;
    private const int RateLimitDelayMs = 350;       // SePay allows 3 req/s
    private static readonly TimeSpan HandleTtl = TimeSpan.FromMinutes(15);

    // Static catalog — SePay has no institutions API.
    private static readonly InstitutionResponse[] Institutions =
    {
        new() { Id = "VCB",  BankCode = "VCB",  Name = "Ngân hàng TMCP Ngoại thương Việt Nam (Vietcombank)" },
        new() { Id = "MB",   BankCode = "MB",   Name = "Ngân hàng TMCP Quân đội (MB Bank)" },
        new() { Id = "TCB",  BankCode = "TCB",  Name = "Ngân hàng TMCP Kỹ thương Việt Nam (Techcombank)" },
        new() { Id = "ACB",  BankCode = "ACB",  Name = "Ngân hàng TMCP Á Châu (ACB)" },
        new() { Id = "BIDV", BankCode = "BIDV", Name = "Ngân hàng TMCP Đầu tư và Phát triển Việt Nam (BIDV)" },
        new() { Id = "VPB",  BankCode = "VPB",  Name = "Ngân hàng TMCP Việt Nam Thịnh Vượng (VPBank)" },
        new() { Id = "VTB",  BankCode = "VTB",  Name = "Ngân hàng TMCP Công thương Việt Nam (VietinBank)" },
        new() { Id = "TPB",  BankCode = "TPB",  Name = "Ngân hàng TMCP Tiên Phong (TPBank)" },
    };

    private readonly FinVietDbContext _db;
    private readonly ISepayClient _sepay;
    private readonly IMemoryCache _cache;
    private readonly IDataProtector _protector;
    private readonly ILogger<LinkedWalletService> _logger;

    public LinkedWalletService(
        FinVietDbContext db,
        ISepayClient sepay,
        IMemoryCache cache,
        IDataProtectionProvider dataProtection,
        ILogger<LinkedWalletService> logger)
    {
        _db = db;
        _sepay = sepay;
        _cache = cache;
        _protector = dataProtection.CreateProtector("FinViet.Sepay.Token.v1");
        _logger = logger;
    }

    public IReadOnlyList<InstitutionResponse> GetInstitutions(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
            return Institutions;

        return Institutions
            .Where(i => string.Equals(i.Country, country.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<ConnectResponse> ConnectAsync(
        ConnectRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SepayToken))
            throw new BadRequestException("sepayToken is required.");

        var sepayToken = request.SepayToken.Trim();

        // Validate the token by actually calling SePay; a bad token surfaces as sepay_unauthorized.
        var response = await _sepay.GetBankAccountsAsync(sepayToken, cancellationToken);

        // Hold the validated token behind an opaque handle for the rest of the link session.
        var accessToken = $"acc_{Guid.NewGuid():N}";
        _cache.Set(AccessKey(accessToken), sepayToken, HandleTtl);

        return new ConnectResponse
        {
            AccessToken = accessToken,
            Accounts = response.Data.Select(MapAccount).ToList()
        };
    }

    public async Task<IReadOnlyList<LinkedAccountResponse>> GetAccountsAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var sepayToken = ResolveAccessToken(accessToken);

        var response = await _sepay.GetBankAccountsAsync(sepayToken, cancellationToken);

        return response.Data.Select(MapAccount).ToList();
    }

    private static LinkedAccountResponse MapAccount(SepayBankAccount a) => new()
    {
        SepayAccountId = a.Id,
        AccountNumber = a.AccountNumber,
        HolderName = a.AccountHolderName ?? a.Label,
        BankCode = a.BankCode ?? a.BankShortName,
        BankName = a.BankFullName ?? a.BankShortName
    };

    public async Task<LinkWalletResponse> LinkAsync(
        Guid customerId,
        Guid walletId,
        LinkWalletRequest request,
        CancellationToken cancellationToken = default)
    {
        var sepayToken = ResolveAccessToken(request.AccessToken);

        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.WalletId == walletId && !w.IsDeleted, cancellationToken);

        if (wallet is null)
            throw new NotFoundException("Wallet", walletId);

        if (wallet.CustomerId != customerId)
            throw new ForbiddenException("You do not have access to this wallet.");

        // Only SePay-linked wallets may bind a token. Basic (and any other) wallet types are
        // rejected here with a 422 — never a 500 — so the FE can surface a clear message.
        if (!string.Equals(wallet.WalletType, SepayLinkedWalletType, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException(
                "Chỉ ví loại liên kết SePay mới gắn được token.", "wallet_not_sepay_linked");

        // Capture the account this token represents (for display); first account by default.
        var accounts = await _sepay.GetBankAccountsAsync(sepayToken, cancellationToken);
        var account = accounts.Data.FirstOrDefault();

        var link = await _db.WalletLinks
            .FirstOrDefaultAsync(l => l.WalletId == walletId, cancellationToken);

        if (link is null)
        {
            link = new WalletLink
            {
                WalletId = walletId,
                CreatedAt = DateTime.UtcNow
            };
            _db.WalletLinks.Add(link);
        }

        link.SepayToken = _protector.Protect(sepayToken);   // encrypted at rest
        link.SepayAccountId = account?.Id;
        link.SepayBankName = account?.BankFullName ?? account?.BankShortName;
        link.SepayAccountMask = Mask(account?.AccountNumber);
        link.SepaySyncStatus = SepaySyncStatus.Ok;
        link.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Multiple sepay_linked wallets pointing at the same SePay account/token are allowed.
            // The legacy v3 schema may still carry a UNIQUE constraint on wallet_links
            // (sepay_account_id / sepay_token); DbInitializer relaxes it, but if an environment
            // hasn't run that yet we degrade to a clean 422 instead of a raw 500.
            _logger.LogWarning(ex,
                "wallet_links unique violation while linking wallet {WalletId}; treat as already-linked account.",
                walletId);
            throw new BusinessRuleException(
                "Tài khoản SePay này đã được liên kết. Vui lòng cập nhật cơ sở dữ liệu để cho phép liên kết nhiều ví.",
                "sepay_account_already_linked");
        }

        return new LinkWalletResponse
        {
            WalletId = walletId,
            SepayAccountId = link.SepayAccountId,
            BankName = link.SepayBankName,
            AccountMask = link.SepayAccountMask
        };
    }

    public async Task<SyncResultResponse> SyncAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken = default)
    {
        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.WalletId == walletId && !w.IsDeleted, cancellationToken);

        if (wallet is null)
            throw new NotFoundException("Wallet", walletId);

        if (wallet.CustomerId != customerId)
            throw new ForbiddenException("You do not have access to this wallet.");

        if (!string.Equals(wallet.WalletType, SepayLinkedWalletType, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException(
                "Chỉ ví liên kết SePay mới đồng bộ được giao dịch.", "wallet_not_sepay_linked");

        var link = await _db.WalletLinks
            .FirstOrDefaultAsync(l => l.WalletId == walletId, cancellationToken);

        if (link is null || string.IsNullOrWhiteSpace(link.SepayToken))
            throw new BusinessRuleException(
                "Ví chưa được liên kết SePay. Hãy gọi /linked-wallets/{id}/link trước.", "wallet_not_linked");

        var sepayToken = UnprotectToken(link.SepayToken);

        var imported = 0;
        var skipped = 0;

        // Polling cursor: resume from the newest SePay transaction we've already imported for
        // this wallet. This used to be cached on wallet_links.sepay_last_synced_txn_id; that
        // column was removed, so we derive the cursor from the transactions already stored.
        // Imports run oldest-first, so the most recently created sepay_sync row carries the
        // newest SePay id. If this returns null we simply re-scan from the start — the
        // ExternalId dedup in TryImportAsync keeps that correct (just less efficient).
        var sinceId = await _db.Transactions
            .Where(t => t.WalletId == walletId
                        && t.EntryMethod == "sepay_sync"
                        && t.ExternalId != null)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.ExternalId)
            .FirstOrDefaultAsync(cancellationToken);

        try
        {
            var page = 1;
            while (true)
            {
                var response = await _sepay.GetTransactionsAsync(
                    sepayToken, sinceId: sinceId, perPage: SyncPageSize, sort: "asc", page: page,
                    cancellationToken: cancellationToken);

                foreach (var tx in response.Data)
                {
                    var (ok, isDuplicate) = await TryImportAsync(wallet, tx, cancellationToken);
                    if (ok) imported++; else if (isDuplicate) skipped++;
                }

                if (response.Meta?.Pagination?.HasMore != true)
                    break;

                page++;
                await Task.Delay(RateLimitDelayMs, cancellationToken);
            }

            link.SepayLastSyncAt = DateTime.UtcNow;
            link.SepaySyncStatus = SepaySyncStatus.Ok;
            link.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not BusinessRuleException and not NotFoundException and not ForbiddenException)
        {
            _logger.LogError(ex, "SePay sync failed for wallet {WalletId}", walletId);
            link.SepaySyncStatus = SepaySyncStatus.Error;
            link.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            throw new BusinessRuleException("Đồng bộ SePay thất bại. Vui lòng thử lại.", "sepay_sync_failed");
        }

        return new SyncResultResponse
        {
            WalletId = walletId,
            Imported = imported,
            Skipped = skipped,
            LastSyncedAt = link.SepayLastSyncAt
        };
    }

    /// <summary>
    /// Inserts one SePay transaction as a wallet transaction (entry_method=sepay_sync,
    /// external_id=SePay id) and adjusts the wallet balance. Returns (imported, isDuplicate).
    /// Dedup is enforced by the uq_tx_external unique index.
    /// </summary>
    private async Task<(bool Imported, bool Duplicate)> TryImportAsync(
        Wallet wallet, SepayTransaction tx, CancellationToken cancellationToken)
    {
        var externalId = tx.Id;

        var exists = await _db.Transactions
            .AnyAsync(t => t.ExternalId == externalId, cancellationToken);
        if (exists)
            return (false, true);

        var isIncome = string.Equals(tx.TransferType, "in", StringComparison.OrdinalIgnoreCase);
        var amount = isIncome ? tx.AmountIn : tx.AmountOut;
        if (amount <= 0)
            return (false, false); // nothing to record

        var transaction = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            CustomerId = wallet.CustomerId ?? Guid.Empty,
            WalletId = wallet.WalletId,
            CategoryId = null,                       // uncategorized; user/AI classifies later
            TransactionType = isIncome ? "income" : "expense",
            EntryMethod = "sepay_sync",
            Amount = amount,
            TransactionDate = ParseSepayDate(tx.TransactionDate),
            Description = tx.TransactionContent,
            Merchant = tx.TransactionContent,
            ExternalId = externalId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Transactions.Add(transaction);

        wallet.Balance = (wallet.Balance ?? 0m) + (isIncome ? amount : -amount);
        wallet.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return (true, false);
        }
        catch (DbUpdateException) // racing unique violation on external_id
        {
            _db.Entry(transaction).State = EntityState.Detached;
            wallet.Balance = (wallet.Balance ?? 0m) - (isIncome ? amount : -amount);
            return (false, true);
        }
    }

    /// <summary>Resolves an opaque accessToken handle to the underlying SePay token.</summary>
    private string ResolveAccessToken(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)
            || !_cache.TryGetValue(AccessKey(accessToken), out string? sepayToken)
            || string.IsNullOrWhiteSpace(sepayToken))
        {
            throw new BadRequestException("accessToken is invalid or has expired. Re-run connect-token/exchange.");
        }

        return sepayToken;
    }

    private string UnprotectToken(string stored)
    {
        try
        {
            return _protector.Unprotect(stored);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt stored SePay token.");
            throw new BusinessRuleException("Token SePay đã lưu không hợp lệ. Hãy liên kết lại ví.", "sepay_token_corrupt");
        }
    }

    /// <summary>True when an EF save failed because of a Postgres unique-constraint violation (23505).</summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string? Mask(string? accountNumber)
        => string.IsNullOrWhiteSpace(accountNumber) || accountNumber.Length <= 4
            ? accountNumber
            : accountNumber[^4..];

    private static DateTime ParseSepayDate(string raw)
        => DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt
            : DateTime.UtcNow;

    private static string AccessKey(string token) => $"sepay:access:{token}";
}
