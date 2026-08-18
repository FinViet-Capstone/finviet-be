using FinViet.Application.DTOs.Buckets;
using FinViet.Application.Features.Buckets.Queries.GetBuckets;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Buckets.Queries.GetBuckets;

public class GetBucketsQueryHandler : IRequestHandler<GetBucketsQuery, IReadOnlyList<BucketResponse>>
{
    private readonly FinVietDbContext _db;
    public GetBucketsQueryHandler(FinVietDbContext db) => _db = db;

    public async Task<IReadOnlyList<BucketResponse>> Handle(GetBucketsQuery request, CancellationToken cancellationToken)
    {
        return await _db.Buckets
            .AsNoTracking()
            .OrderBy(b => b.SortOrder)
            .Select(b => new BucketResponse
            {
                Id = b.Id,
                NameVi = b.NameVi,
                NameEn = b.NameEn,
                Color = b.Color,
                Icon = b.Icon,
                SortOrder = b.SortOrder,
                IsLocked = b.IsLocked,
                DefaultPct = b.DefaultPct
            })
            .ToListAsync(cancellationToken);
    }
}
