using FinViet.Application.DTOs.Categories;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class CategoryService : ICategoryService
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INCOME", "EXPENSE"
    };

    private static readonly HashSet<string> AllowedExpenseClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "NEEDS", "WANTS", "SAVINGS"
    };

    private readonly FinVietDbContext _dbContext;

    public CategoryService(FinVietDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(
        string? type,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Categories.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = NormalizeType(type);
            query = query.Where(c => c.Type == normalizedType);
        }

        return await query
            .OrderBy(c => c.CategoryName)
            .Select(c => ToResponse(c))
            .ToListAsync(cancellationToken);
    }

    public async Task<CategoryResponse?> GetCategoryByIdAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var category = await _dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CategoryId == categoryId, cancellationToken);

        return category is null ? null : ToResponse(category);
    }

    public async Task<CategoryResponse> CreateCategoryAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CategoryName))
            throw new ValidationException("Category name is required.");

        var normalizedType = NormalizeType(request.Type);
        var normalizedExpenseClass = NormalizeExpenseClass(request.ExpenseClass);

        var trimmedName = request.CategoryName.Trim();
        var duplicateName = await CategoryNameExistsAsync(trimmedName, normalizedType, null, cancellationToken);

        if (duplicateName)
            throw new ValidationException("Category name already exists for this type.");

        var category = new Category
        {
            CategoryId = Guid.NewGuid(),
            CategoryName = trimmedName,
            Type = normalizedType,
            IsMandatory = request.IsMandatory,
            ExpenseClass = normalizedExpenseClass,
            ModelBucket = string.IsNullOrWhiteSpace(request.ModelBucket) ? null : request.ModelBucket.Trim()
        };

        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(category);
    }

    public async Task<CategoryResponse?> UpdateCategoryAsync(
        Guid categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(c => c.CategoryId == categoryId, cancellationToken);

        if (category is null)
            return null;

        if (request.Type is not null)
            category.Type = NormalizeType(request.Type);

        if (request.CategoryName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.CategoryName))
                throw new ValidationException("Category name cannot be empty.");

            var newName = request.CategoryName.Trim();
            var duplicateName = await CategoryNameExistsAsync(newName, category.Type, categoryId, cancellationToken);

            if (duplicateName)
                throw new ValidationException("Category name already exists for this type.");

            category.CategoryName = newName;
        }

        if (request.IsMandatory.HasValue)
            category.IsMandatory = request.IsMandatory.Value;

        if (request.ExpenseClass is not null)
            category.ExpenseClass = NormalizeExpenseClass(request.ExpenseClass);

        if (request.ModelBucket is not null)
            category.ModelBucket = string.IsNullOrWhiteSpace(request.ModelBucket) ? null : request.ModelBucket.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(category);
    }

    public async Task<bool> DeleteCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(c => c.CategoryId == categoryId, cancellationToken);

        if (category is null)
            return false;

        var hasTransactions = await _dbContext.Transactions
            .AnyAsync(t => t.CategoryId == categoryId, cancellationToken);

        if (hasTransactions)
            throw new ValidationException("Cannot delete category because it is referenced by transactions.");

        _dbContext.Categories.Remove(category);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string NormalizeType(string type)
    {
        var normalized = type.Trim().ToUpperInvariant();
        if (!AllowedTypes.Contains(normalized))
            throw new ValidationException("Category type must be one of: INCOME, EXPENSE.");

        return normalized;
    }

    private static string? NormalizeExpenseClass(string? expenseClass)
    {
        if (string.IsNullOrWhiteSpace(expenseClass))
            return null;

        var normalized = expenseClass.Trim().ToUpperInvariant();
        if (!AllowedExpenseClasses.Contains(normalized))
            throw new ValidationException("Expense class must be one of: NEEDS, WANTS, SAVINGS.");

        return normalized;
    }

    private async Task<bool> CategoryNameExistsAsync(
        string categoryName,
        string type,
        Guid? excludedCategoryId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Categories.AnyAsync(
            c => c.Type == type
                 && (!excludedCategoryId.HasValue || c.CategoryId != excludedCategoryId.Value)
                 && EF.Functions.ILike(c.CategoryName, categoryName),
            cancellationToken);
    }

    private static CategoryResponse ToResponse(Category category)
        => new()
        {
            CategoryId = category.CategoryId,
            CategoryName = category.CategoryName,
            Type = category.Type,
            IsMandatory = category.IsMandatory ?? false,
            ExpenseClass = category.ExpenseClass,
            ModelBucket = category.ModelBucket
        };
}
