using FinViet.Application.DTOs.Subscriptions;
using MediatR;

namespace FinViet.Application.Features.Subscriptions.Queries.GetVNPayReturnStatus;

public record GetVNPayReturnStatusQuery(
    IReadOnlyDictionary<string, string> VnpParams
) : IRequest<VNPayReturnStatusDto>;
