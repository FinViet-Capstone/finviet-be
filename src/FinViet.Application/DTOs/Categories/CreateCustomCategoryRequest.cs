namespace FinViet.Application.DTOs.Categories;

/// <summary>
/// Customer-scoped category creation (no admin approval). Always creates an expense category —
/// the FE flow only ever picks a bucket, which has no meaning for income categories.
/// </summary>
public class CreateCustomCategoryRequest
{
    public string Name { get; set; } = string.Empty;

    /// <summary>One of: needs, wants, savings.</summary>
    public string Bucket { get; set; } = string.Empty;

    public string? Color { get; set; }
}
