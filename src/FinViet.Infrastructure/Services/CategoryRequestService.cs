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

    private static readonly HashSet<string> AllowedExpenseClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "NEEDS", "WANTS", "SAVINGS"
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
        var normalizedExpenseClass = NormalizeExpenseClass(request.ExpenseClass);
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
                 && r.Status == "PENDING"
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
            ExpenseClass = normalizedExpenseClass,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            Status = "PENDING",
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

        if (entity.Status != "PENDING")
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
                CategoryId = Guid.NewGuid(),
                CategoryName = entity.CategoryName,
                Type = entity.Type,
                IsMandatory = false,
                ExpenseClass = entity.ExpenseClass
            };
            _dbContext.Categories.Add(category);
        }

        entity.Status = "APPROVED";
        entity.ReviewedBy = adminId;
        entity.ReviewNote = string.IsNullOrWhiteSpace(request.ReviewNote) ? null : request.ReviewNote.Trim();
        entity.CreatedCategoryId = category.CategoryId;
        entity.ReviewedAt = DateTime.UtcNow;

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

        if (entity.Status != "PENDING")
            throw new ValidationException($"Request has already been {entity.Status.ToLowerInvariant()}.");

        entity.Status = "REJECTED";
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

    private static string? NormalizeExpenseClass(string? expenseClass)
    {
        if (string.IsNullOrWhiteSpace(expenseClass))
            return null;

        var normalized = expenseClass.Trim().ToUpperInvariant();
        if (!AllowedExpenseClasses.Contains(normalized))
            throw new ValidationException("Expense class must be one of: NEEDS, WANTS, SAVINGS.");

        return normalized;
    }

    private static string NormalizeStatus(string status)
    {
        var normalized = status.Trim().ToUpperInvariant();
        if (normalized is not ("PENDING" or "APPROVED" or "REJECTED"))
            throw new ValidationException("Status must be one of: PENDING, APPROVED, REJECTED.");

        return normalized;
    }

    private static CategoryRequestResponse ToResponse(CategoryRequest r)
        => new()
        {
            RequestId = r.RequestId,
            CustomerId = r.CustomerId,
            CategoryName = r.CategoryName,
            Type = r.Type,
            ExpenseClass = r.ExpenseClass,
            Note = r.Note,
            Status = r.Status,
            ReviewNote = r.ReviewNote,
            CreatedCategoryId = r.CreatedCategoryId,
            CreatedAt = r.CreatedAt,
            ReviewedAt = r.ReviewedAt
        };
}
