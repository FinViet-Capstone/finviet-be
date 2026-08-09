using FinViet.Application.DTOs;
using MediatR;

namespace FinViet.Application.Features.Profile.Commands.ScheduleIncomeAllocationChange;

public record ScheduleIncomeAllocationChangeCommand(
    Guid CustomerId,
    decimal MonthlyIncome,
    int NeedsPct,
    int WantsPct,
    int SavingsPct
) : IRequest<IncomeAllocationEntryDto>;
