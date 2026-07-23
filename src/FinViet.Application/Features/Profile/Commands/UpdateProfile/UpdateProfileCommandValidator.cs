using FluentValidation;

namespace FinViet.Application.Features.Profile.Commands.UpdateProfile;

public class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(100).WithMessage("Full name must not exceed 100 characters.");

        RuleFor(x => x.MonthlyIncomeExpected)
            .GreaterThanOrEqualTo(0).When(x => x.MonthlyIncomeExpected.HasValue)
            .WithMessage("Monthly income must be a positive number.");

        // The 50-30-20 allocation is set as a trio (settings screen sends all three
        // sliders together) — require all-or-nothing so the stored split never has a
        // silently-missing bucket, and enforce it sums to 100 like the FE validation does.
        RuleFor(x => x)
            .Must(x => x.NeedsPct.HasValue && x.WantsPct.HasValue && x.SavingsPct.HasValue)
            .When(x => x.NeedsPct.HasValue || x.WantsPct.HasValue || x.SavingsPct.HasValue)
            .WithMessage("needsPct, wantsPct and savingsPct must be provided together.")
            .WithName("AllocationPct");

        RuleFor(x => x.NeedsPct)
            .InclusiveBetween(0, 100).When(x => x.NeedsPct.HasValue)
            .WithMessage("needsPct must be between 0 and 100.");
        RuleFor(x => x.WantsPct)
            .InclusiveBetween(0, 100).When(x => x.WantsPct.HasValue)
            .WithMessage("wantsPct must be between 0 and 100.");
        RuleFor(x => x.SavingsPct)
            .InclusiveBetween(0, 100).When(x => x.SavingsPct.HasValue)
            .WithMessage("savingsPct must be between 0 and 100.");

        RuleFor(x => x)
            .Must(x => x.NeedsPct!.Value + x.WantsPct!.Value + x.SavingsPct!.Value == 100)
            .When(x => x.NeedsPct.HasValue && x.WantsPct.HasValue && x.SavingsPct.HasValue)
            .WithMessage("needsPct, wantsPct and savingsPct must sum to 100.")
            .WithName("AllocationPct");
    }
}
