using System.Globalization;
using System.Text;
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
        "income", "expense"
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

        // cat_savings_goal is auto-only — never offered in the manual picker.
        query = query.Where(c => c.CategoryId != "cat_savings_goal");

        return await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.CategoryName)
            .Select(c => ToResponse(c))
            .ToListAsync(cancellationToken);
    }

    public async Task<CategoryResponse?> GetCategoryByIdAsync(
        string categoryId,
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

        var slug = await GenerateUniqueSlugAsync(trimmedName, cancellationToken);

        var category = new Category
        {
            CategoryId = slug,
            CategoryName = trimmedName,
            NameVi = trimmedName,
            NameEn = request.NameEn,
            Type = normalizedType,
            IsMandatory = request.IsMandatory,
            ExpenseClass = normalizedExpenseClass,
            Icon = request.Icon,
            Color = request.Color,
            SortOrder = request.SortOrder
        };

        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(category);
    }

    public async Task<CategoryResponse?> UpdateCategoryAsync(
        string categoryId,
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

        if (request.NameEn is not null)
            category.NameEn = request.NameEn;

        if (request.Icon is not null)
            category.Icon = request.Icon;

        if (request.Color is not null)
            category.Color = request.Color;

        if (request.SortOrder.HasValue)
            category.SortOrder = request.SortOrder.Value;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(category);
    }

    public async Task<bool> DeleteCategoryAsync(
        string categoryId,
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
        var normalized = type.Trim().ToLowerInvariant();
        if (!AllowedTypes.Contains(normalized))
            throw new ValidationException("Category type must be one of: income, expense.");

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
        string? excludedCategoryId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Categories.AnyAsync(
            c => c.Type == type
                 && (excludedCategoryId == null || c.CategoryId != excludedCategoryId)
                 && EF.Functions.ILike(c.CategoryName, categoryName),
            cancellationToken);
    }

    /// <summary>Builds a unique slug id like "cat_an_uong"; appends a numeric suffix on collision.</summary>
    private async Task<string> GenerateUniqueSlugAsync(string name, CancellationToken cancellationToken)
    {
        var baseSlug = "cat_" + Slugify(name);
        var slug = baseSlug;
        var suffix = 2;

        while (await _dbContext.Categories.AnyAsync(c => c.CategoryId == slug, cancellationToken))
        {
            slug = $"{baseSlug}_{suffix}";
            suffix++;
        }

        return slug.Length > 40 ? slug[..40] : slug;
    }

    private static string Slugify(string input)
    {
        // Strip Vietnamese diacritics, lowercase, keep [a-z0-9], collapse to underscores.
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (ch == 'đ' || ch == 'Đ')
                sb.Append('d');
            else
                sb.Append(ch);
        }

        var ascii = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var slug = new StringBuilder();
        var lastUnderscore = false;
        foreach (var ch in ascii)
        {
            if (ch >= 'a' && ch <= 'z' || ch >= '0' && ch <= '9')
            {
                slug.Append(ch);
                lastUnderscore = false;
            }
            else if (!lastUnderscore)
            {
                slug.Append('_');
                lastUnderscore = true;
            }
        }

        return slug.ToString().Trim('_');
    }

    private static CategoryResponse ToResponse(Category category)
        => new()
        {
            CategoryId = category.CategoryId,
            CategoryName = category.CategoryName,
            NameVi = category.NameVi,
            NameEn = category.NameEn,
            Type = category.Type,
            IsMandatory = category.IsMandatory ?? false,
            ExpenseClass = category.ExpenseClass,
            Icon = category.Icon,
            Color = category.Color,
            SortOrder = category.SortOrder
        };
}
