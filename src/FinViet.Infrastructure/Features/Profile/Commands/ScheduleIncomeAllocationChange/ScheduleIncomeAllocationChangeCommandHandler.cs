using FinViet.Application.DTOs;
using FinViet.Application.Features.Profile.Commands.ScheduleIncomeAllocationChange;
using FinViet.Application.Interfaces;
using MediatR;

namespace FinViet.Infrastructure.Features.Profile.Commands.ScheduleIncomeAllocationChange;

public class ScheduleIncomeAllocationChangeCommandHandler
    : IRequestHandler<ScheduleIncomeAllocationChangeCommand, IncomeAllocationEntryDto>
{
    private readonly IIncomeAllocationService _incomeAllocationService;

    public ScheduleIncomeAllocationChangeCommandHandler(IIncomeAllocationService incomeAllocationService)
        => _incomeAllocationService = incomeAllocationService;

    public Task<IncomeAllocationEntryDto> Handle(
        ScheduleIncomeAllocationChangeCommand request, CancellationToken cancellationToken)
        => _incomeAllocationService.ScheduleNextMonthAsync(
            request.CustomerId,
            request.MonthlyIncome,
            request.NeedsPct,
            request.WantsPct,
            request.SavingsPct,
            cancellationToken);
}
