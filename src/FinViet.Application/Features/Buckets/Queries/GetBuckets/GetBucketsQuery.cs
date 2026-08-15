using FinViet.Application.DTOs.Buckets;
using MediatR;

namespace FinViet.Application.Features.Buckets.Queries.GetBuckets;

public record GetBucketsQuery : IRequest<IReadOnlyList<BucketResponse>>;
