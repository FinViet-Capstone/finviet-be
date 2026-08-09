using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Exceptions;
using FinViet.Infrastructure.Persistence.Entities;

namespace FinViet.Infrastructure.Services;

internal static class WalletRules
{
    private const string BasicWalletType = "basic";

    private static readonly HashSet<string> LegacyBasicWalletTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CASH", "BANK_ACCOUNT", "CREDIT_CARD", "E_WALLET", "INVESTMENT"
    };

    internal static void ValidateCreate(CreateWalletRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WalletName))
            throw new ValidationException("Wallet name is required.");

        if (string.IsNullOrWhiteSpace(request.WalletType))
            throw new ValidationException("Wallet type is required.");

        if (request.InitialBalance < 0)
            throw new ValidationException("Initial balance cannot be negative.");
    }

    internal static void ValidateUpdate(UpdateWalletRequest request)
    {
        if (request.WalletName is not null && string.IsNullOrWhiteSpace(request.WalletName))
            throw new ValidationException("Wallet name cannot be empty.");

        if (request.WalletType is not null)
            throw new ValidationException("Wallet type cannot be changed after creation.");
    }

    internal static string NormalizeWalletType(string walletType)
    {
        var normalized = walletType.Trim();
        if (!normalized.Equals(BasicWalletType, StringComparison.OrdinalIgnoreCase)
            && !LegacyBasicWalletTypes.Contains(normalized))
        {
            throw new ValidationException("Wallet type must be one of: basic.");
        }

        return BasicWalletType;
    }

    internal static string NormalizeStoredWalletType(string walletType)
        => LegacyBasicWalletTypes.Contains(walletType)
            ? BasicWalletType
            : walletType.ToLowerInvariant();

    internal static Guid GetRequiredCustomerId(Wallet wallet)
        => wallet.CustomerId
           ?? throw new InvalidOperationException($"Wallet {wallet.WalletId} is missing customer_id.");

    internal static decimal GetRequiredBalance(Wallet wallet)
        => wallet.Balance
           ?? throw new InvalidOperationException($"Wallet {wallet.WalletId} is missing balance.");
}
