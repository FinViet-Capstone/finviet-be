using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Infrastructure.Persistence.Repositories;

namespace FinViet.Application.UnitTests;

// TC-SPLIT-01..12 — the rules that decide whether one transaction may be split across
// categories (council review 1: "phân chia một khoản thu/chi phát sinh").
//
// These are worth testing exhaustively because a split *replaces* the original row: the parts
// must sum back to it exactly, or the wallet balance silently moves. Everything under test is
// pure, so no database is needed.
public class SplitTransactionTests
{
    private static SplitPartRequest Part(decimal amount, string? categoryId = "cat_food") =>
        new() { CategoryId = categoryId, Amount = amount };

    /// <summary>A normal manual expense of 500.000đ in a basic wallet — the happy baseline.</summary>
    private static void Validate(
        IReadOnlyList<SplitPartRequest> parts,
        decimal amount = 500_000m,
        string entryMethod = "manual",
        Guid? transferPairId = null,
        string? categoryId = "cat_food",
        string? walletType = "basic")
        => TransactionRepository.ValidateSplit(
            entryMethod, transferPairId, categoryId, amount, walletType, parts);

    private static string CodeOf(Action act)
    {
        var ex = Assert.Throws<BusinessRuleException>(act);
        return ex.Code!;
    }

    // ── The split that should work ────────────────────────────────────────────────

    [Fact]
    public void PartsSummingToTheOriginal_AreAccepted()
    {
        Validate(new[] { Part(300_000m), Part(200_000m, "cat_transport") });
    }

    [Fact]
    public void ManyPartsAreFine_AsLongAsTheySumBack()
    {
        Validate(new[] { Part(250_000m), Part(150_000m), Part(75_000m), Part(25_000m) });
    }

    [Fact]
    public void PartsWithNoCategoryAreAllowed()
    {
        // Category is nullable on transactions, so an uncategorised part is legitimate —
        // splitting is about the amounts, and the user can categorise afterwards.
        Validate(new[] { Part(300_000m, null), Part(200_000m, null) });
    }

    // ── Amount arithmetic — this is what protects the wallet balance ──────────────

    [Fact]
    public void PartsTotallingLessThanTheOriginal_AreRejected()
    {
        // Would leave 100.000đ unaccounted for and shrink what the wallet says was spent.
        Assert.Equal("split_total_mismatch",
            CodeOf(() => Validate(new[] { Part(300_000m), Part(100_000m) })));
    }

    [Fact]
    public void PartsTotallingMoreThanTheOriginal_AreRejected()
    {
        Assert.Equal("split_total_mismatch",
            CodeOf(() => Validate(new[] { Part(300_000m), Part(300_000m) })));
    }

    [Fact]
    public void ATotalOffByOneDong_IsStillRejected()
    {
        // Exact equality on decimal, not a tolerance: one đồng of drift per split is still
        // the balance moving, and it compounds.
        Assert.Equal("split_total_mismatch",
            CodeOf(() => Validate(new[] { Part(300_000m), Part(199_999m) })));
    }

    [Fact]
    public void FractionalAmountsThatSumExactly_AreAccepted()
    {
        // A third of 1.000đ can't be written exactly, but the client is free to send parts
        // that do sum back — the rule is about the total, not about round numbers.
        Validate(new[] { Part(333.33m), Part(333.33m), Part(333.34m) }, amount: 1_000m);
    }

    [Fact]
    public void AZeroPart_IsRejected()
    {
        Assert.Equal("split_part_not_positive",
            CodeOf(() => Validate(new[] { Part(500_000m), Part(0m) })));
    }

    [Fact]
    public void ANegativePart_IsRejected()
    {
        // Negatives could make any total add up while inventing money on one category.
        Assert.Equal("split_part_not_positive",
            CodeOf(() => Validate(new[] { Part(600_000m), Part(-100_000m) })));
    }

    [Fact]
    public void ASinglePart_IsRejected()
    {
        // Not a split — it would just be a category change, which update already does.
        Assert.Equal("split_needs_two_parts",
            CodeOf(() => Validate(new[] { Part(500_000m) })));
    }

    // ── Transactions that must never be split ────────────────────────────────────

    [Fact]
    public void ProviderSyncedTransactions_CannotBeSplit()
    {
        // Sync owns these rows; replacing one would be undone or duplicated on the next sync.
        Assert.Equal("synced_transaction_locked",
            CodeOf(() => Validate(
                new[] { Part(300_000m), Part(200_000m) }, entryMethod: "sepay_sync")));
    }

    [Fact]
    public void TransferLegs_CannotBeSplit()
    {
        // The paired leg would be left pointing at a row that no longer exists.
        Assert.Equal("transfer_cannot_be_split",
            CodeOf(() => Validate(
                new[] { Part(300_000m), Part(200_000m) }, transferPairId: Guid.NewGuid())));
    }

    [Fact]
    public void SavingGoalTransactions_CannotBeSplit()
    {
        // These are the ledger behind a goal's balance; re-categorising parts would desync it.
        Assert.Equal("goal_transaction_cannot_be_split",
            CodeOf(() => Validate(
                new[] { Part(300_000m), Part(200_000m) }, categoryId: "cat_savings_goal")));
    }

    [Fact]
    public void BankLinkedWallets_AreReadOnly()
    {
        Assert.Equal("linked_wallet_read_only",
            CodeOf(() => Validate(
                new[] { Part(300_000m), Part(200_000m) }, walletType: "sepay_linked")));
    }

    [Fact]
    public void BlockingRulesRunBeforeAmountChecks()
    {
        // A synced transaction with a wrong total should report *why it can't be split at
        // all*, not send the user off fixing amounts on something they can never split.
        Assert.Equal("synced_transaction_locked",
            CodeOf(() => Validate(
                new[] { Part(1m), Part(1m) }, entryMethod: "sepay_sync")));
    }
}
