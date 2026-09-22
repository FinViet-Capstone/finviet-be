using FluentValidation;

namespace FinViet.Application.Features.Subscriptions.Commands.CreatePayment;

public class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty()
            .WithMessage("Idempotency-Key header is required for payment requests.");
    }
}
