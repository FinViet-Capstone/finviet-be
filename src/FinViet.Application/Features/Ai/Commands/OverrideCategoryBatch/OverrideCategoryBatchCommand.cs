using FinViet.Application.DTOs.Ai;
using MediatR;

namespace FinViet.Application.Features.Ai.Commands.OverrideCategoryBatch;

public record OverrideCategoryBatchCommand(
    Guid CustomerId,
    OverrideCategoryBatchRequest Request
) : IRequest<OverrideCategoryBatchResponse>;
