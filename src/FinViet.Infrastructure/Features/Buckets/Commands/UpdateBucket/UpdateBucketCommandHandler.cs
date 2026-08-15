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

        await _db.SaveChangesAsync(cancellationToken);

        return new BucketResponse
        {
            Id = bucket.Id,
            NameVi = bucket.NameVi,
            NameEn = bucket.NameEn,
            Color = bucket.Color,
            Icon = bucket.Icon,
            SortOrder = bucket.SortOrder,
            IsLocked = bucket.IsLocked
        };
    }
}
