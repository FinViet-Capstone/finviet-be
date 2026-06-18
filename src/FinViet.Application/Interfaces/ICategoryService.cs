using FinViet.Application.DTOs.Categories;

namespace FinViet.Application.Interfaces;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(
        string? type,
        CancellationToken cancellationToken = default);

    Task<CategoryResponse?> GetCategoryByIdAsync(
        string categoryId,
        CancellationToken cancellationToken = default);

    Task<CategoryResponse> CreateCategoryAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default);

    Task<CategoryResponse?> UpdateCategoryAsync(
        string categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteCategoryAsync(
        string categoryId,
        CancellationToken cancellationToken = default);
}
