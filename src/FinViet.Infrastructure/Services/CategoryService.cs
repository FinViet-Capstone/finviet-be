using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FinViet.Application.DTOs.Categories;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class CategoryService : ICategoryService
{
    private const string SavingsGoalCategoryId = "cat_savings_goal";

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "income", "expense"
    };

    private static readonly HashSet<string> AllowedExpenseClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "needs", "wants", "savings"
    };

    private readonly FinVietDbContext _dbContext;

    public CategoryService(FinVietDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(
        string? type,
        Guid? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Categories.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = NormalizeType(type);
            query = query.Where(c => c.Type == normalizedType);
        }

        query = query.Where(c => c.CategoryId != "cat_savings_goal");

        var categories = await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.CategoryName)
            .ToListAsync(cancellationToken);

        var overrides = await GetCustomerBucketOverridesAsync(customerId, cancellationToken);
        return categories.Select(c => ToResponse(c, overrides.GetValueOrDefault(c.CategoryId))).ToList();
    }

    public async Task<CategoryResponse?> GetCategoryByIdAsync(
        string categoryId,
        Guid? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var category = await _dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CategoryId == categoryId, cancellationToken);

        if (category is null)
            return null;

        var overrides = await GetCustomerBucketOverridesAsync(customerId, cancellationToken);
        return ToResponse(category, overrides.GetValueOrDefault(categoryId));
    }

    public async Task<CategoryResponse> SetCustomerBucketAsync(
        Guid customerId,
        string categoryId,
        string bucketId,
        CancellationToken cancellationToken = default)
    {
        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(c => c.CategoryId == categoryId, cancellationToken);

        if (category is null)
            throw new NotFoundException("Category not found.");

        if (!string.Equals(category.Type, "expense", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Only expense categories can be assigned to a bucket.");

        if (string.Equals(category.CategoryId, SavingsGoalCategoryId, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Saving goal contributions cannot be reassigned to a different bucket.");

        var normalizedBucket = bucketId?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedBucket) || !AllowedExpenseClasses.Contains(normalizedBucket))
            throw new ValidationException("Bucket must be one of: needs, wants, savings.");

        var customerCategory = await _dbContext.CustomerCategories
            .FirstOrDefaultAsync(x => x.CustomerId == customerId && x.CategoryId == categoryId, cancellationToken);

        if (customerCategory is null)
        {
            _dbContext.CustomerCategories.Add(new CustomerCategory
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                CategoryId = categoryId,
                BucketId = normalizedBucket,
                // "request" now simply means "customer-set override" — the admin
                // category-request approval flow that this label used to imply was removed.
                Source = "request",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            customerCategory.BucketId = normalizedBucket;
            customerCategory.IsActive = true;
            customerCategory.Source = "request";
            customerCategory.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(category, normalizedBucket);
    }

    public async Task<CategoryResponse> ResetCustomerBucketAsync(
        Guid customerId,
        string categoryId,
        CancellationToken cancellationToken = default)
    {
        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(c => c.CategoryId == categoryId, cancellationToken);

        if (category is null)
            throw new NotFoundException("Category not found.");

        var customerCategory = await _dbContext.CustomerCategories
            .FirstOrDefaultAsync(x => x.CustomerId == customerId && x.CategoryId == categoryId, cancellationToken);

        if (customerCategory is not null && customerCategory.IsActive)
        {
            customerCategory.IsActive = false;
            customerCategory.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return ToResponse(category, null);
    }

    private async Task<Dictionary<string, string>> GetCustomerBucketOverridesAsync(
        Guid? customerId,
        CancellationToken cancellationToken)
    {
        if (customerId is null)
            return new Dictionary<string, string>();

        return await _dbContext.CustomerCategories
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId.Value && x.IsActive)
            .ToDictionaryAsync(x => x.CategoryId, x => x.BucketId, cancellationToken);
    }

    public async Task<CategoryResponse> CreateCategoryAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var nameVi = FirstNonEmpty(request.NameVi, request.CategoryName);
        if (string.IsNullOrWhiteSpace(nameVi))
            throw new ValidationException("Category name is required.");

        var normalizedType = NormalizeType(request.Type);
        var normalizedExpenseClass = NormalizeExpenseClass(request.ExpenseClass, normalizedType);
        var trimmedNameVi = nameVi.Trim();
        var trimmedNameEn = string.IsNullOrWhiteSpace(request.NameEn) ? trimmedNameVi : request.NameEn.Trim();
        var categoryId = string.IsNullOrWhiteSpace(request.CategoryId)
            ? await GenerateUniqueSlugAsync(trimmedNameEn, cancellationToken)
            : request.CategoryId.Trim();

        var duplicateId = await _dbContext.Categories.AnyAsync(c => c.CategoryId == categoryId, cancellationToken);
        if (duplicateId)
            throw new ValidationException("Category id already exists.");

        var duplicateName = await CategoryNameExistsAsync(trimmedNameVi, normalizedType, null, cancellationToken);
        if (duplicateName)
            throw new ValidationException("Category name already exists for this type.");

        var category = new Category
        {
            CategoryId = categoryId,
            CategoryName = trimmedNameVi,
            NameVi = trimmedNameVi,
            NameEn = trimmedNameEn,
            Type = normalizedType,
            IsMandatory = request.IsMandatory,
            DefaultBucket = normalizedExpenseClass,
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

        if (request.CategoryName is not null || request.NameVi is not null)
        {
            var newName = FirstNonEmpty(request.NameVi, request.CategoryName);
            if (string.IsNullOrWhiteSpace(newName))
                throw new ValidationException("Category name cannot be empty.");

            var trimmed = newName.Trim();
            var duplicateName = await CategoryNameExistsAsync(trimmed, category.Type, categoryId, cancellationToken);

            if (duplicateName)
                throw new ValidationException("Category name already exists for this type.");

            category.CategoryName = trimmed;
            category.NameVi = trimmed;
        }

        if (request.IsMandatory.HasValue)
            category.IsMandatory = request.IsMandatory.Value;

        if (request.ExpenseClass is not null)
            category.DefaultBucket = NormalizeExpenseClass(request.ExpenseClass, category.Type);

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

    private static string? NormalizeExpenseClass(string? expenseClass, string type)
    {
        if (type == "income")
            return null;

        if (string.IsNullOrWhiteSpace(expenseClass))
            throw new ValidationException("Expense class is required for expense categories.");

        var normalized = expenseClass.Trim().ToLowerInvariant();
        if (!AllowedExpenseClasses.Contains(normalized))
            throw new ValidationException("Expense class must be one of: needs, wants, savings.");

        return normalized;
    }

    private async Task<bool> CategoryNameExistsAsync(
        string categoryName,
        string type,
        string? excludedCategoryId,
        CancellationToken cancellationToken)
    {
        // CategoryName is mapped to the name_vi column; NameVi is unmapped (Ignored),
        // so the duplicate check must run against CategoryName only.
        return await _dbContext.Categories.AnyAsync(
            c => c.Type == type
                 && (excludedCategoryId == null || c.CategoryId != excludedCategoryId)
                 && EF.Functions.ILike(c.CategoryName, categoryName),
            cancellationToken);
    }

    private async Task<string> GenerateUniqueSlugAsync(string name, CancellationToken cancellationToken)
    {
        var baseSlug = "cat_" + Slugify(name);
        var slug = baseSlug;
        var i = 2;

        while (await _dbContext.Categories.AnyAsync(c => c.CategoryId == slug, cancellationToken))
        {
            slug = $"{baseSlug}_{i}";
            i++;
        }

        return slug;
    }

    private static string Slugify(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        var ascii = builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        ascii = Regex.Replace(ascii, "[^a-z0-9]+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(ascii) ? Guid.NewGuid().ToString("N")[..8] : ascii;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static CategoryResponse ToResponse(Category category, string? customerBucketOverride = null)
        => new()
        {
            CategoryId = category.CategoryId,
            CategoryName = category.CategoryName,
            // CategoryName is mapped to the name_vi column; NameVi itself is unmapped.
            NameVi = category.CategoryName,
            NameEn = category.NameEn,
            Type = category.Type,
            IsMandatory = category.IsMandatory ?? false,
            // The customer's own bucket override (customer_categories) wins over the
            // category's global default when the caller is an authenticated customer.
            ExpenseClass = customerBucketOverride ?? category.DefaultBucket,
            Icon = category.Icon,
            Color = category.Color,
            SortOrder = category.SortOrder
        };
}
