using FinViet.Application.DTOs.Buckets;
using MediatR;

namespace FinViet.Application.Features.Buckets.Commands.UpdateBucket;

public record UpdateBucketCommand(
    string Id,
    string? NameVi,
    string? NameEn,
    string? Color,
    string? Icon,
    int? SortOrder,
    decimal? DefaultPct) : IRequest<BucketResponse>;
