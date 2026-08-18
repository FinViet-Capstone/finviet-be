using FinViet.Application.Common;
using FinViet.Application.DTOs.CategoryCorrections;
using FinViet.Application.Features.CategoryCorrections.Queries.GetCategoryCorrections;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.CategoryCorrections.Queries.GetCategoryCorrections;

public class GetCategoryCorrectionsQueryHandler
    : IRequestHandler<GetCategoryCorrectionsQuery, PagedResult<CategoryCorrectionResponseDto>>
{
    private readonly FinVietDbContext _db;
    public GetCategoryCorrectionsQueryHandler(FinVietDbContext db) => _db = db;

    public async Task<PagedResult<CategoryCorrectionResponseDto>> Handle(
        GetCategoryCorrectionsQuery request, CancellationToken cancellationToken)
    {
        var filter = request.Query;
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;

        var query = _db.CategoryCorrectionLogs
            .AsNoTracking()
            .Include(l => l.Customer)
            .Include(l => l.Transaction)
            .Include(l => l.CorrectedCategory)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.CategoryId))
            query = query.Where(l => l.CorrectedCategoryId == filter.CategoryId);

        // Same UTC start-of-day/exclusive-next-day convention as TransactionRepository.GetPagedAsync.
        if (filter.CreatedAtFrom.HasValue)
        {
            var from = DateTime.SpecifyKind(filter.CreatedAtFrom.Value.Date, DateTimeKind.Utc);
            query = query.Where(l => l.CreatedAt >= from);
        }
        if (filter.CreatedAtTo.HasValue)
        {
            var toExclusive = DateTime.SpecifyKind(filter.CreatedAtTo.Value.Date, DateTimeKind.Utc).AddDays(1);
            query = query.Where(l => l.CreatedAt < toExclusive);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new CategoryCorrectionResponseDto
            {
                LogId = l.LogId,
                CustomerId = l.CustomerId,
                CustomerEmail = l.Customer != null ? l.Customer.Email : null,
                TransactionId = l.TransactionId,
                TransactionDescription = l.Transaction != null ? l.Transaction.Description : null,
                Amount = l.Transaction != null ? l.Transaction.Amount : (decimal?)null,
                AdminId = l.AdminId,
                CorrectedCategoryId = l.CorrectedCategoryId,
                CorrectedCategoryName = l.CorrectedCategory != null
                    ? (l.CorrectedCategory.NameVi ?? l.CorrectedCategory.CategoryName)
                    : null,
                OriginalAiGuess = l.OriginalAiGuess,
                CreatedAt = l.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<CategoryCorrectionResponseDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
            Items = items
        };
    }
}
