using FinViet.Infrastructure.Persistence;
using FinViet.Infrastructure.Persistence.Entities;
using Xunit;

namespace FinViet.Application.UnitTests;

public class TransactionCategorizationTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private static Transaction Sepay(
        string? source = null, string? categoryId = null, string? guess = null,
        DateTime? classifiedAt = null, string entryMethod = "sepay_sync", string type = "expense") => new()
    {
        TransactionId = Guid.NewGuid(),
        EntryMethod = entryMethod,
        TransactionType = type,
        CategoryId = categoryId,
        AiClassificationSource = source,
        AiCategoryGuess = guess,
        AiClassifiedAt = classifiedAt
    };

    [Fact]
    public void Rule1_ManualSource_IsReviewed_EvenWithCategoryOrNonSepay()
    {
        Assert.Equal("reviewed", TransactionCategorization.Derive(Sepay("manual", categoryId: "food"), Now));
        Assert.Equal("reviewed", TransactionCategorization.Derive(Sepay("manual", entryMethod: "manual"), Now));
    }

    [Fact]
    public void Rule2_CategorySet_IsApplied_BeforeNoneAndSuggestion()
    {
        Assert.Equal("applied", TransactionCategorization.Derive(Sepay("ai_auto", categoryId: "food"), Now));
        Assert.Equal("applied", TransactionCategorization.Derive(Sepay(categoryId: "food", entryMethod: "csv"), Now));
    }

    [Theory]
    [InlineData("csv", "expense")]
    [InlineData("sepay_sync", "income")]
    [InlineData("manual", "expense")]
    public void Rule3_NotSepayExpense_IsNone(string entryMethod, string type)
    {
        var t = Sepay("ai_suggestion", guess: "food", entryMethod: entryMethod, type: type);
        Assert.Equal("none", TransactionCategorization.Derive(t, Now));
    }

    [Fact]
    public void Rule4_SuggestionWithGuess_IsSuggested() =>
        Assert.Equal("suggested", TransactionCategorization.Derive(Sepay("ai_suggestion", guess: "food"), Now));

    [Fact]
    public void Rule5_SuggestionWithoutGuess_IsUnsure() =>
        Assert.Equal("unsure", TransactionCategorization.Derive(Sepay("ai_suggestion"), Now));

    [Fact]
    public void Rule6_QueuedWithinTenMinutes_IsPending_AndExpiresToFailed()
    {
        Assert.Equal("pending", TransactionCategorization.Derive(Sepay(classifiedAt: Now.AddMinutes(-9)), Now));
        Assert.Equal("pending", TransactionCategorization.Derive(Sepay(classifiedAt: Now.AddMinutes(-10)), Now));
        Assert.Equal("failed", TransactionCategorization.Derive(Sepay(classifiedAt: Now.AddMinutes(-11)), Now));
    }

    [Fact]
    public void Rule7_EverythingElse_IsFailed()
    {
        Assert.Equal("failed", TransactionCategorization.Derive(Sepay("fallback"), Now));
        Assert.Equal("failed", TransactionCategorization.Derive(Sepay(), Now));
    }

    [Fact]
    public void Matches_PredicateAgreesWithDerive_ForEveryStatus()
    {
        var rows = new[]
        {
            Sepay("manual"), Sepay("ai_auto", categoryId: "c"), Sepay("ai_suggestion", guess: "g", entryMethod: "csv"),
            Sepay("ai_suggestion", guess: "g"), Sepay("ai_suggestion"), Sepay(classifiedAt: Now.AddMinutes(-1)),
            Sepay(classifiedAt: Now.AddMinutes(-30)), Sepay("fallback"), Sepay()
        };

        foreach (var status in TransactionCategorization.AllStatuses)
        {
            var predicate = TransactionCategorization.Matches(new[] { status }, Now).Compile();
            foreach (var row in rows)
                Assert.Equal(TransactionCategorization.Derive(row, Now) == status, predicate(row));
        }

        var inboxStatuses = new[] { "pending", "suggested", "unsure", "failed" };
        var inbox = TransactionCategorization.Matches(inboxStatuses, Now).Compile();
        foreach (var row in rows)
            Assert.Equal(inboxStatuses.Contains(TransactionCategorization.Derive(row, Now)), inbox(row));
    }
}
