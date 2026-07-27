using FinViet.Application.DTOs.Categories;

namespace FinViet.Application.Interfaces;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(
        string? type,
        Guid? customerId = null,
        CancellationToken cancellationToken = default);

    Task<CategoryResponse?> GetCategoryByIdAsync(
        string categoryId,
        Guid? customerId = null,
        CancellationToken cancellationToken = default);

    Task<CategoryResponse> CreateCategoryAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Customer-scoped category creation, no admin approval — inserts a real <c>Category</c> row
    /// (id <c>custom_&lt;uuid&gt;</c>) so Transaction/Budget FKs resolve, and seeds an active
    /// <c>CustomerCategory</c> override so it's immediately usable in the creator's chosen bucket.
    /// The category is private to its creator (see <see cref="GetCategoriesAsync"/>).
    /// </summary>
    Task<CategoryResponse> CreateCustomCategoryAsync(
        Guid customerId,
        CreateCustomCategoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a customer's own custom category. Returns false (→ 404) for a seeded <c>cat_*</c>
    /// category, one the customer doesn't own, or one that doesn't exist — same "doesn't exist to
    /// you" framing as <see cref="GetCategoryByIdAsync"/>'s visibility scoping. Throws if the
    /// category is referenced by transactions, matching <see cref="DeleteCategoryAsync"/>.
    /// </summary>
    Task<bool> DeleteCustomCategoryAsync(
        Guid customerId,
        string categoryId,
        CancellationToken cancellationToken = default);

    Task<CategoryResponse?> UpdateCategoryAsync(
        string categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteCategoryAsync(
        string categoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lets a customer reassign which bucket ("hũ") an expense category counts against for
    /// them, without any admin approval — writes straight to <c>customer_categories</c>.
    /// </summary>
    Task<CategoryResponse> SetCustomerBucketAsync(
        Guid customerId,
        string categoryId,
        string bucketId,
        CancellationToken cancellationToken = default);

    /// <summary>Clears the customer's bucket override, reverting the category to its global default.</summary>
    Task<CategoryResponse> ResetCustomerBucketAsync(
        Guid customerId,
        string categoryId,
        CancellationToken cancellationToken = default);
}
