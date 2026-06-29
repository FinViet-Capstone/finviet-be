using System.Globalization;
using System.Text.Json;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.LinkedWallets;
using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Interfaces;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.ExternalServices.Finverse;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.Services;

/// <summary>
/// Finverse-backed linked wallets. Stores the per-login-identity tokens encrypted on wallet_links
/// (reusing the SePay columns) as a small JSON blob so a single table serves both providers.
/// </summary>
public class FinverseLinkService : IFinverseLinkService
{
    private const string FinverseWalletType = "finverse_linked";
    private const string FinverseEntryMethod = "finverse_sync";
    private const int MaxWalletsPerCustomer = 10;

    private readonly FinVietDbContext _db;
    private readonly IFinverseClient _finverse;
    private readonly IDataProtector _protector;
    private readonly FinverseOptions _options;
    private readonly ILogger<FinverseLinkService> _logger;

    public FinverseLinkService(
        FinVietDbContext db,
        IFinverseClient finverse,
        IDataProtectionProvider dataProtection,
        IOptions<FinverseOptions> options,
        ILogger<FinverseLinkService> logger)
    {
        _db = db;
        _finverse = finverse;
        _protector = dataProtection.CreateProtector("FinViet.Finverse.Token.v1");
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>What we persist (encrypted) to re-fetch later without re-linking.</summary>
    private sealed record StoredLink(string LoginIdentityId, string? RefreshToken, string AccountId);

    public async Task<FinverseLinkResponse> CreateLinkAsync(
        Guid customerId, CancellationToken cancellationToken = default)
    {
        var customerToken = (await _finverse.GetCustomerTokenAsync(cancellationToken)).AccessToken;
        var state = $"fv_{Guid.NewGuid():N}";

        var link = await _finverse.CreateLinkAsync(
            customerToken, customerId.ToString(), state, cancellationToken);

        if (string.IsNullOrWhiteSpace(link.LinkUrl))
            throw new BusinessRuleException("Finverse không trả về link đăng nhập.", "finverse_no_link_url");

        return new FinverseLinkResponse
        {
            LinkUrl = link.LinkUrl,
            State = state,
            RedirectUri = _options.RedirectUri,
        };
    }

    public async Task<IReadOnlyList<WalletResponse>> ExchangeAsync(
        Guid customerId, FinverseExchangeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new BadRequestException("code is required.");

        var customerToken = (await _finverse.GetCustomerTokenAsync(cancellationToken)).AccessToken;
        var exchange = await _finverse.ExchangeCodeAsync(customerToken, request.Code.Trim(), cancellationToken);
        var loginIdentityToken = exchange.AccessToken;

        var accounts = await _finverse.GetAccountsAsync(loginIdentityToken, cancellationToken);
        if (accounts.Accounts.Count == 0)
            throw new BusinessRuleException("Không tìm thấy tài khoản ngân hàng nào.", "finverse_no_accounts");

        var existingCount = await _db.Wallets
            .CountAsync(w => w.CustomerId == customerId && !w.IsDeleted, cancellationToken);

        var created = new List<WalletResponse>();
        var existingNames = await _db.Wallets
            .Where(w => w.CustomerId == customerId && !w.IsDeleted)
            .Select(w => w.WalletName)
            .ToListAsync(cancellationToken);

        foreach (var account in accounts.Accounts)
        {
            if (existingCount + created.Count >= MaxWalletsPerCustomer)
                break;

            var baseName = account.AccountName ?? account.AccountNumberMasked ?? "Ngân hàng";
            var walletName = UniqueName(baseName, existingNames);
            existingNames.Add(walletName);

            var wallet = new Wallet
            {
                WalletId = Guid.NewGuid(),
                CustomerId = customerId,
                WalletName = walletName,
                WalletType = FinverseWalletType,
                Balance = account.Balance?.Value ?? 0m,
            };
            _db.Wallets.Add(wallet);

            var blob = JsonSerializer.Serialize(
                new StoredLink(exchange.LoginIdentityId, exchange.RefreshToken, account.AccountId));

            _db.WalletLinks.Add(new WalletLink
            {
                WalletId = wallet.WalletId,
                SepayToken = _protector.Protect(blob),
                SepayAccountId = account.AccountId,
                SepayBankName = account.AccountName,
                SepayAccountMask = account.AccountNumberMasked,
                SepaySyncStatus = SepaySyncStatus.Ok,
                SepayLastSyncAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });

            await _db.SaveChangesAsync(cancellationToken);

            await ImportTransactionsAsync(wallet, loginIdentityToken, account.AccountId, cancellationToken);

            created.Add(new WalletResponse
            {
                WalletId = wallet.WalletId,
                CustomerId = customerId,
                WalletName = wallet.WalletName,
                WalletType = FinverseWalletType,
                Balance = wallet.Balance ?? 0m,
            });
        }

        return created;
    }

    /// <summary>
    /// Imports an account's transactions as history rows. The wallet balance is NOT adjusted here —
    /// Finverse returns the authoritative current balance on the account, which we set at creation.
    /// </summary>
    private async Task ImportTransactionsAsync(
        Wallet wallet, string loginIdentityToken, string accountId, CancellationToken cancellationToken)
    {
        FinverseTransactionsResponse txns;
        try
        {
            txns = await _finverse.GetTransactionsAsync(loginIdentityToken, accountId, 0, 500, cancellationToken);
        }
        catch (Exception ex)
        {
            // Linking already succeeded; a transaction fetch failure shouldn't roll the wallet back.
            _logger.LogWarning(ex, "Finverse transaction import failed for wallet {WalletId}", wallet.WalletId);
            return;
        }

        foreach (var tx in txns.Transactions)
        {
            if (string.IsNullOrWhiteSpace(tx.TransactionId)) continue;

            var value = tx.Amount?.Value ?? 0m;
            if (value == 0m) continue;

            var exists = await _db.Transactions.AnyAsync(t => t.ExternalId == tx.TransactionId, cancellationToken);
            if (exists) continue;

            var isIncome = value > 0m;
            _db.Transactions.Add(new Transaction
            {
                TransactionId = Guid.NewGuid(),
                CustomerId = wallet.CustomerId ?? Guid.Empty,
                WalletId = wallet.WalletId,
                CategoryId = null,
                TransactionType = isIncome ? "income" : "expense",
                EntryMethod = FinverseEntryMethod,
                Amount = Math.Abs(value),
                TransactionDate = ParseDate(tx.TransactionDate ?? tx.PostedDate),
                Description = tx.Description,
                Merchant = tx.Description,
                ExternalId = tx.TransactionId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException) // racing unique on external_id
            {
                _db.ChangeTracker.Clear();
            }
        }
    }

    private static string UniqueName(string baseName, List<string> taken)
    {
        if (!taken.Contains(baseName, StringComparer.OrdinalIgnoreCase)) return baseName;
        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!taken.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
        }
        return $"{baseName} {Guid.NewGuid():N}"[..40];
    }

    private static DateTime ParseDate(string? raw)
        => DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt
            : DateTime.UtcNow;
}
