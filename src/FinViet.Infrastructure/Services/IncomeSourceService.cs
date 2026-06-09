using FinViet.Application.DTOs.IncomeSources;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class IncomeSourceService : IIncomeSourceService
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SALARY", "BUSINESS", "INVESTMENT", "BONUS", "OTHER"
    };

    private readonly FinVietDbContext _dbContext;

    public IncomeSourceService(FinVietDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<IncomeSourceResponse>> GetIncomeSourcesAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.IncomeSources
            .AsNoTracking()
            .Where(s => s.CustomerId == customerId)
            .OrderBy(s => s.Name)
            .Select(s => new IncomeSourceResponse
            {
                SourceId = s.SourceId,
                CustomerId = s.CustomerId!.Value,
                Name = s.Name,
                Type = s.Type
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IncomeSourceResponse?> GetIncomeSourceByIdAsync(
        Guid customerId,
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        var source = await _dbContext.IncomeSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CustomerId == customerId && s.SourceId == sourceId, cancellationToken);

        return source is null ? null : ToResponse(source);
    }

    public async Task<IncomeSourceResponse> CreateIncomeSourceAsync(
        Guid customerId,
        CreateIncomeSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("Income source name is required.");

        await EnsureCustomerExistsAsync(customerId, cancellationToken);

        var normalizedType = NormalizeType(request.Type);
        var trimmedName = request.Name.Trim();
        var duplicateName = await NameExistsAsync(customerId, trimmedName, null, cancellationToken);

        if (duplicateName)
            throw new ValidationException("Income source name already exists for this customer.");

        var source = new IncomeSource
        {
            SourceId = Guid.NewGuid(),
            CustomerId = customerId,
            Name = trimmedName,
            Type = normalizedType
        };

        _dbContext.IncomeSources.Add(source);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(source);
    }

    public async Task<IncomeSourceResponse?> UpdateIncomeSourceAsync(
        Guid customerId,
        Guid sourceId,
        UpdateIncomeSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = await _dbContext.IncomeSources
            .FirstOrDefaultAsync(s => s.CustomerId == customerId && s.SourceId == sourceId, cancellationToken);

        if (source is null)
            return null;

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ValidationException("Income source name cannot be empty.");

            var newName = request.Name.Trim();
            var duplicateName = await NameExistsAsync(customerId, newName, sourceId, cancellationToken);

            if (duplicateName)
                throw new ValidationException("Income source name already exists for this customer.");

            source.Name = newName;
        }

        if (request.Type is not null)
            source.Type = NormalizeType(request.Type);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(source);
    }

    public async Task<bool> DeleteIncomeSourceAsync(
        Guid customerId,
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        var source = await _dbContext.IncomeSources
            .FirstOrDefaultAsync(s => s.CustomerId == customerId && s.SourceId == sourceId, cancellationToken);

        if (source is null)
            return false;

        var hasTransactions = await _dbContext.Transactions
            .AnyAsync(t => t.SourceId == sourceId, cancellationToken);

        if (hasTransactions)
            throw new ValidationException("Cannot delete income source because it is referenced by transactions.");

        _dbContext.IncomeSources.Remove(source);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task EnsureCustomerExistsAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Customers.AnyAsync(c => c.CustomerId == customerId, cancellationToken);
        if (!exists)
            throw new NotFoundException("Customer not found.");
    }

    private static string? NormalizeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return null;

        var normalized = type.Trim().ToUpperInvariant();
        if (!AllowedTypes.Contains(normalized))
            throw new ValidationException("Income source type must be one of: SALARY, BUSINESS, INVESTMENT, BONUS, OTHER.");

        return normalized;
    }

    private async Task<bool> NameExistsAsync(
        Guid customerId,
        string name,
        Guid? excludedSourceId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.IncomeSources.AnyAsync(
            s => s.CustomerId == customerId
                 && (!excludedSourceId.HasValue || s.SourceId != excludedSourceId.Value)
                 && EF.Functions.ILike(s.Name, name),
            cancellationToken);
    }

    private static IncomeSourceResponse ToResponse(IncomeSource source)
        => new()
        {
            SourceId = source.SourceId,
            CustomerId = source.CustomerId!.Value,
            Name = source.Name,
            Type = source.Type
        };
}
