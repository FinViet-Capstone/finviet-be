using FluentValidation;

namespace FinViet.Application.Features.SubscriptionPlans.Commands.CreateSubscriptionPlan;

public class CreateSubscriptionPlanCommandValidator : AbstractValidator<CreateSubscriptionPlanCommand>
{
    public CreateSubscriptionPlanCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .Matches("^[a-z0-9_]+$").WithMessage("Code must be lowercase letters, digits, and underscores only.")
            .MaximumLength(20);

        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);

        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);

        RuleFor(x => x.BillingIntervalMonths).GreaterThan((short)0);

        RuleFor(x => x.Features).NotNull();
        RuleForEach(x => x.Features).NotEmpty();
    }
}
