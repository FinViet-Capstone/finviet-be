using FinViet.Application.DTOs;
using MediatR;

namespace FinViet.Application.Features.Profile.Commands.ScheduleIncomeAllocationChange;

public record ScheduleIncomeAllocationChangeCommand(
    Guid CustomerId,
    decimal MonthlyIncome,
    decimal NeedsPct,
    decimal WantsPct,
    decimal SavingsPct
) : IRequest<IncomeAllocationEntryDto>;
