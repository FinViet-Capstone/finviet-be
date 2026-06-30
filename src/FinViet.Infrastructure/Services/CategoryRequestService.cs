using FinViet.Application.DTOs.CategoryRequests;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class CategoryRequestService : ICategoryRequestService
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INCOME", "EXPENSE"
    };

    // suggested_bucket_id is a FK to buckets(id); valid ids are the lowercase slugs.
    private static readonly HashSet<string> AllowedBuckets = new(StringComparer.OrdinalIgnoreCase)
    {
        "needs", "wants", "savings"
    };

    private readonly FinVietDbContext _dbContext;

    public CategoryRequestService(FinVietDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CategoryRequestResponse> SubmitAsync(
        Guid customerId,
        CreateCategoryRequestRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CategoryName))
            throw new ValidationException("Category name is required.");

        var customerExists = await _dbContext.Customers.AnyAsync(c => c.CustomerId == customerId, cancellationToken);
        if (!customerExists)
            throw new NotFoundException("Customer not found.");

        var normalizedType = NormalizeType(request.Type);
        var normalizedBucket = NormalizeBucket(request.ExpenseClass, normalizedType);
        var trimmedName = request.CategoryName.Trim();

        // If the category already exists, there's no point requesting it.
        var alreadyExists = await _dbContext.Categories.AnyAsync(
            c => c.Type == normalizedType && EF.Functions.ILike(c.CategoryName, trimmedName),
            cancellationToken);

        if (alreadyExists)
            throw new ValidationException("A category with this name and type already exists.");

        // Prevent duplicate pending requests for the same name/type.
        var duplicatePending = await _dbContext.CategoryRequests.AnyAsync(
            r => r.CustomerId == customerId
                 && r.Status == "pending"
                 && r.Type == normalizedType
                 && EF.Functions.ILike(r.CategoryName, trimmedName),
            cancellationToken);

        if (duplicatePending)
            throw new ValidationException("You already have a pending request for this category.");

        var entity = new CategoryRequest
        {
            RequestId = Guid.NewGuid(),
            CustomerId = customerId,
            CategoryName = trimmedName,
            Type = normalizedType,
            SuggestedBucketId = normalizedBucket,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.CategoryRequests.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(entity);
    }

    public async Task<IReadOnlyList<CategoryRequestResponse>> GetMyRequestsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.CategoryRequests
            .AsNoTracking()
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(ToResponse).ToList();
    }

    public async Task<IReadOnlyList<CategoryRequestResponse>> GetRequestsAsync(
        string? status,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.CategoryRequests.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = NormalizeStatus(status);
            query = query.Where(r => r.Status == normalizedStatus);
        }

        var entities = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(ToResponse).ToList();
    }

    public async Task<CategoryRequestResponse?> ApproveAsync(
        Guid adminId,
        Guid requestId,
        ReviewCategoryRequestRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CategoryRequests
            .FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);

        if (entity is null)
            return null;

        // entity.Status comes back as the canonical lower-case enum label (e.g. "pending"),
        // so compare case-insensitively rather than against the upper-case literal.
        if (!string.Equals(entity.Status, "pending", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"Request has already been {entity.Status.ToLowerInvariant()}.");

        // Create the real category from the request (unless an identical one appeared meanwhile).
        var existing = await _dbContext.Categories.FirstOrDefaultAsync(
            c => c.Type == entity.Type && EF.Functions.ILike(c.CategoryName, entity.CategoryName),
            cancellationToken);

        Category category;
        if (existing is not null)
        {
            category = existing;
        }
        else
        {
            category = new Category
            {
                // "cat_" + a GUID without separators is 36 characters, already
                // within the category-id column limit; slicing it to 40 throws.
                CategoryId = $"cat_{Guid.NewGuid():N}",
                CategoryName = entity.CategoryName,
                NameVi = entity.CategoryName,
                NameEn = entity.CategoryName,
                Type = entity.Type.ToLowerInvariant(),
                IsMandatory = false,
                DefaultBucket = entity.SuggestedBucketId?.ToLowerInvariant()
            };
            _dbContext.Categories.Add(category);
        }

        entity.Status = "approved";
        entity.ReviewedBy = adminId;
        entity.ReviewNote = string.IsNullOrWhiteSpace(request.ReviewNote) ? null : request.ReviewNote.Trim();
        entity.CreatedCategoryId = category.CategoryId;
        entity.ReviewedAt = DateTime.UtcNow;

        // A successfully approved expense request must immediately be usable by
        // its requester. The global category library remains shared, while the
        // requester receives a customer-specific bucket assignment marked as a
        // request-origin row.
        if (string.Equals(category.Type, "expense", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(category.CategoryId, "cat_savings_goal", StringComparison.OrdinalIgnoreCase))
        {
            var bucketId = category.DefaultBucket ?? entity.SuggestedBucketId;
            if (string.IsNullOrWhiteSpace(bucketId))
                throw new ValidationException("Approved expense categories require a bucket assignment.");

            var customerCategory = await _dbContext.CustomerCategories
                .FirstOrDefaultAsync(
                    x => x.CustomerId == entity.CustomerId && x.CategoryId == category.CategoryId,
                    cancellationToken);

            if (customerCategory is null)
            {
                _dbContext.CustomerCategories.Add(new CustomerCategory
                {
                    Id = Guid.NewGuid(),
                    CustomerId = entity.CustomerId,
                    CategoryId = category.CategoryId,
                    BucketId = bucketId,
                    Source = "request",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                customerCategory.BucketId = bucketId;
                customerCategory.IsActive = true;
                customerCategory.Source = "request";
                customerCategory.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(entity);
    }

    public async Task<CategoryRequestResponse?> RejectAsync(
        Guid adminId,
        Guid requestId,
        ReviewCategoryRequestRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CategoryRequests
            .FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);

        if (entity is null)
            return null;

        // entity.Status comes back as the canonical lower-case enum label (e.g. "pending"),
        // so compare case-insensitively rather than against the upper-case literal.
        if (!string.Equals(entity.Status, "pending", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"Request has already been {entity.Status.ToLowerInvariant()}.");

        entity.Status = "rejected";
        entity.ReviewedBy = adminId;
        entity.ReviewNote = string.IsNullOrWhiteSpace(request.ReviewNote) ? null : request.ReviewNote.Trim();
        entity.ReviewedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(entity);
    }

    private static string NormalizeType(string type)
    {
        var normalized = type.Trim().ToUpperInvariant();
        if (!AllowedTypes.Contains(normalized))
            throw new ValidationException("Category type must be one of: INCOME, EXPENSE.");

        return normalized;
    }

    private static string? NormalizeBucket(string? bucket, string normalizedType)
    {
        if (normalizedType == "INCOME")
            return null;

        if (string.IsNullOrWhiteSpace(bucket))
            throw new ValidationException("Suggested bucket is required for expense categories.");

        // Store the lowercase slug so it satisfies the suggested_bucket_id → buckets(id) FK.
        var normalized = bucket.Trim().ToLowerInvariant();
        if (!AllowedBuckets.Contains(normalized))
            throw new ValidationException("Suggested bucket must be one of: needs, wants, savings.");

        return normalized;
    }

    private static string NormalizeStatus(string status)
    {
        // Store/compare the canonical lower-case enum label so in-memory comparisons against
        // values read back through PgEnumStringConverter line up.
        var normalized = status.Trim().ToLowerInvariant();
        if (normalized is not ("pending" or "approved" or "rejected"))
            throw new ValidationException("Status must be one of: pending, approved, rejected.");

        return normalized;
    }

    private static CategoryRequestResponse ToResponse(CategoryRequest r)
        => new()
        {
            RequestId = r.RequestId,
            CustomerId = r.CustomerId,
            CategoryName = r.CategoryName,
            Type = r.Type,
            ExpenseClass = r.SuggestedBucketId,
            Note = r.Note,
            Status = r.Status,
            ReviewNote = r.ReviewNote,
            CreatedCategoryId = r.CreatedCategoryId,
            CreatedAt = r.CreatedAt,
            ReviewedAt = r.ReviewedAt
        };
}
