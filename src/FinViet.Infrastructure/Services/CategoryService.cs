using FinViet.Application.DTOs.Categories;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class CategoryService : ICategoryService
{
    // Distinguishes customer-created categories from seeded cat_* ones — see CreateCustomCategoryAsync.
    private const string CustomCategoryIdPrefix = "custom_";

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
            var normalizedType = CategoryRules.NormalizeType(type);
            query = query.Where(c => c.Type == normalizedType);
        }

        query = query.Where(c => c.CategoryId != CategoryRules.SavingsGoalCategoryId);

        var categories = await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.CategoryName)
            .ToListAsync(cancellationToken);

        var overrides = await GetCustomerBucketOverridesAsync(customerId, cancellationToken);

        // Custom categories (id prefix "custom_") are private to their creator — the global
        // Category table has no owner column, so "is this mine" is "do I have an active
        // customer_categories row for it", seeded at creation time (see CreateCustomCategoryAsync).
        var visible = categories.Where(c => IsVisibleTo(c.CategoryId, overrides));

        return visible.Select(c => ToResponse(c, overrides.GetValueOrDefault(c.CategoryId))).ToList();
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

        if (!IsVisibleTo(category.CategoryId, overrides))
            return null;

        return ToResponse(category, overrides.GetValueOrDefault(categoryId));
    }

    /// <summary>
    /// A seeded <c>cat_*</c> category is visible to everyone; a <c>custom_*</c> one only to a
    /// customer who has an active <c>customer_categories</c> row for it. Pure/no I/O so it's
    /// unit-testable without a database.
    /// </summary>
    internal static bool IsVisibleTo(string categoryId, IReadOnlyDictionary<string, string> customerBucketOverrides)
        => !categoryId.StartsWith(CustomCategoryIdPrefix, StringComparison.Ordinal)
           || customerBucketOverrides.ContainsKey(categoryId);

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

        CategoryRules.EnsureCustomerBucketCanBeSet(category.CategoryId, category.Type);
        var normalizedBucket = CategoryRules.NormalizeCustomerBucket(bucketId);

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
                // A persona source denotes a customer-specific bucket override.
                Source = "persona",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            customerCategory.BucketId = normalizedBucket;
            customerCategory.IsActive = true;
            customerCategory.Source = "persona";
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
        var nameVi = CategoryRules.FirstNonEmpty(request.NameVi, request.CategoryName);
        if (string.IsNullOrWhiteSpace(nameVi))
            throw new ValidationException("Category name is required.");

        var normalizedType = CategoryRules.NormalizeType(request.Type);
        var normalizedExpenseClass = CategoryRules.NormalizeExpenseClass(request.ExpenseClass, normalizedType);
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

    public async Task<CategoryResponse> CreateCustomCategoryAsync(
        Guid customerId,
        CreateCustomCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Category name is required.");

        var normalizedBucket = CategoryRules.NormalizeCustomerBucket(request.Bucket);

        var duplicateName = await CategoryNameExistsAsync(name, "expense", null, cancellationToken);
        if (duplicateName)
            throw new ValidationException("Category name already exists for this type.");

        // Icon must come from POST /api/categories/icons — reject anything else so a client can't
        // smuggle an arbitrary external URL into a field the FE renders directly.
        if (request.Icon is not null && !request.Icon.StartsWith("/category-icons/", StringComparison.Ordinal))
            throw new ValidationException("Icon must be a URL returned by POST /api/categories/icons.");

        // Guid.NewGuid() collisions aren't a practical concern, unlike the name-based admin slug.
        // "N" (no dashes) keeps the id at 39 chars — categories.id is varchar(40), and the
        // dashed form (43 chars) made every insert fail with a Postgres length error.
        var categoryId = $"{CustomCategoryIdPrefix}{Guid.NewGuid():N}";

        var category = new Category
        {
            CategoryId = categoryId,
            CategoryName = name,
            NameVi = name,
            NameEn = name,
            Type = "expense",
            IsMandatory = false,
            DefaultBucket = normalizedBucket,
            Icon = request.Icon,
            Color = request.Color
        };
        _dbContext.Categories.Add(category);

        // Seed the creator's own override immediately so the category is usable in their chosen
        // bucket without a follow-up PUT .../bucket call — also what makes it visible to them
        // (see IsVisibleTo): a custom category has no "global default" separate from this.
        _dbContext.CustomerCategories.Add(new CustomerCategory
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            CategoryId = categoryId,
            BucketId = normalizedBucket,
            Source = "system",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(category, normalizedBucket);
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
            category.Type = CategoryRules.NormalizeType(request.Type);

        if (request.CategoryName is not null || request.NameVi is not null)
        {
            var newName = CategoryRules.FirstNonEmpty(request.NameVi, request.CategoryName);
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
            category.DefaultBucket = CategoryRules.NormalizeExpenseClass(request.ExpenseClass, category.Type);

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

    public async Task<bool> DeleteCustomCategoryAsync(
        Guid customerId,
        string categoryId,
        CancellationToken cancellationToken = default)
    {
        // Same ownership test as IsVisibleTo: a seeded cat_* category or a custom_* one this
        // customer doesn't have an active override for isn't theirs to delete via this endpoint.
        if (!categoryId.StartsWith(CustomCategoryIdPrefix, StringComparison.Ordinal))
            return false;

        var owns = await _dbContext.CustomerCategories.AnyAsync(
            x => x.CustomerId == customerId && x.CategoryId == categoryId && x.IsActive,
            cancellationToken);
        if (!owns)
            return false;

        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(c => c.CategoryId == categoryId, cancellationToken);
        if (category is null)
            return false;

        // Matches DeleteCategoryAsync's rule — don't let a deletion quietly turn transaction
        // history "uncategorized".
        var hasTransactions = await _dbContext.Transactions
            .AnyAsync(t => t.CategoryId == categoryId, cancellationToken);
        if (hasTransactions)
            throw new ValidationException("Cannot delete category because it is referenced by transactions.");

        _dbContext.Categories.Remove(category);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
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
        var baseSlug = "cat_" + CategoryRules.Slugify(name);
        var slug = baseSlug;
        var i = 2;

        while (await _dbContext.Categories.AnyAsync(c => c.CategoryId == slug, cancellationToken))
        {
            slug = $"{baseSlug}_{i}";
            i++;
        }

        return slug;
    }

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
