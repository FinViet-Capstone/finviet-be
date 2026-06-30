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
    }
}
