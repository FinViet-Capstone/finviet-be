using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Buckets;
using FinViet.Application.Features.Buckets.Commands.UpdateBucket;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Buckets.Commands.UpdateBucket;

public class UpdateBucketCommandHandler : IRequestHandler<UpdateBucketCommand, BucketResponse>
{
    private readonly FinVietDbContext _db;
    public UpdateBucketCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<BucketResponse> Handle(UpdateBucketCommand request, CancellationToken cancellationToken)
    {
        // Same inline range check as UpdateScoringCriterionCommandHandler — no atomic
        // sum-to-100 enforcement here either; the frontend validates the merged set before
        // sending each PATCH, since 3 independent PATCHes give the server no single point to
        // check the total against.
        if (request.DefaultPct is < 0 or > 100)
            throw new BadRequestException("defaultPct must be between 0 and 100.");

        var bucket = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken);
        if (bucket is null)
            throw new NotFoundException("Bucket", request.Id);

        // Admin can edit every bucket, including the locked "savings" row — IsLocked is
        // deliberately not enforced here per product direction (backend-gaps.md item 2).
        if (request.NameVi is not null) bucket.NameVi = request.NameVi;
        if (request.NameEn is not null) bucket.NameEn = request.NameEn;
        if (request.Color is not null) bucket.Color = request.Color;
        if (request.Icon is not null) bucket.Icon = request.Icon;
        if (request.SortOrder.HasValue) bucket.SortOrder = request.SortOrder;
        if (request.DefaultPct.HasValue) bucket.DefaultPct = request.DefaultPct;

        await _db.SaveChangesAsync(cancellationToken);

        return new BucketResponse
        {
            Id = bucket.Id,
            NameVi = bucket.NameVi,
            NameEn = bucket.NameEn,
            Color = bucket.Color,
            Icon = bucket.Icon,
            SortOrder = bucket.SortOrder,
            IsLocked = bucket.IsLocked,
            DefaultPct = bucket.DefaultPct
        };
    }
}
