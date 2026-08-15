using FluentValidation;

namespace FinViet.Application.Features.Subscriptions.Commands.SubscribeToPlan;

public class SubscribeToPlanCommandValidator : AbstractValidator<SubscribeToPlanCommand>
{
    public SubscribeToPlanCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();

        RuleFor(x => x.ReturnUrl)
            .NotEmpty().WithMessage("ReturnUrl is required.")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("ReturnUrl must be an absolute URL.");
    }
}
