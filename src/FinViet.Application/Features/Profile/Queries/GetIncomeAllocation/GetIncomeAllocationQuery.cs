using FinViet.Application.DTOs;
using MediatR;

namespace FinViet.Application.Features.Profile.Queries.GetIncomeAllocation;

/// <summary>
/// <paramref name="Month"/> (optional, <c>yyyy-MM</c>) resolves <c>Current</c> for that arbitrary
/// month instead of today's; <c>Pending</c> is only meaningful relative to the real current
/// month, so it's always null when <paramref name="Month"/> is provided. Omit to preserve the
/// original behavior (today's current + next month's draft).
/// </summary>
public record GetIncomeAllocationQuery(Guid CustomerId, string? Month = null) : IRequest<IncomeAllocationSummaryDto>;
