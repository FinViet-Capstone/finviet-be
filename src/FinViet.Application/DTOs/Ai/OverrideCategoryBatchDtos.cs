namespace FinViet.Application.DTOs.Ai;

public class OverrideCategoryBatchItem
{
    public Guid TransactionId { get; set; }
    public string CategoryId { get; set; } = string.Empty;
}

/// <summary>Override many transactions' categories in one request (e.g. accept all AI suggestions).</summary>
public class OverrideCategoryBatchRequest
{
    public List<OverrideCategoryBatchItem> Items { get; set; } = new();
}

public static class OverrideBatchOutcomes
{
    public const string Ok = "ok";
    public const string NotFound = "not_found";
    public const string CategoryUnavailable = "category_unavailable";
    public const string NotEligible = "not_eligible";
}

public record OverrideCategoryBatchItemResult(Guid TransactionId, string Outcome);

public record OverrideCategoryBatchResponse(IReadOnlyList<OverrideCategoryBatchItemResult> Results);
