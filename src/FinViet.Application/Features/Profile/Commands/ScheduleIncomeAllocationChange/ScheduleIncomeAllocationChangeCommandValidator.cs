using FluentValidation;

namespace FinViet.Application.Features.Profile.Commands.ScheduleIncomeAllocationChange;

public class ScheduleIncomeAllocationChangeCommandValidator : AbstractValidator<ScheduleIncomeAllocationChangeCommand>
{
    public ScheduleIncomeAllocationChangeCommandValidator()
    {
        RuleFor(x => x.MonthlyIncome)
            .GreaterThanOrEqualTo(0).WithMessage("Monthly income must be a positive number.");

        RuleFor(x => x.NeedsPct).InclusiveBetween(0, 100).WithMessage("needsPct must be between 0 and 100.");
        RuleFor(x => x.WantsPct).InclusiveBetween(0, 100).WithMessage("wantsPct must be between 0 and 100.");
        RuleFor(x => x.SavingsPct).InclusiveBetween(0, 100).WithMessage("savingsPct must be between 0 and 100.");

        RuleFor(x => x)
            .Must(x => x.NeedsPct + x.WantsPct + x.SavingsPct == 100)
            .WithMessage("needsPct, wantsPct and savingsPct must sum to 100.")
            .WithName("AllocationPct");
    }
}
