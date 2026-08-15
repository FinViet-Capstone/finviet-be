using FluentValidation;

namespace FinViet.Application.Features.SubscriptionPlans.Commands.UpdateSubscriptionPlan;

public class UpdateSubscriptionPlanCommandValidator : AbstractValidator<UpdateSubscriptionPlanCommand>
{
    public UpdateSubscriptionPlanCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BillingIntervalMonths).GreaterThan((short)0);
        RuleFor(x => x.Features).NotNull();
        RuleForEach(x => x.Features).NotEmpty();
    }
}
