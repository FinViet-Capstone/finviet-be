using FluentValidation;

namespace FinViet.Application.Features.Auth.Commands.ChangeAdminPassword;

public class ChangeAdminPasswordCommandValidator : AbstractValidator<ChangeAdminPasswordCommand>
{
    public ChangeAdminPasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.");

        // Same policy as ChangePasswordCommandValidator/ResetPasswordCommandValidator, for consistency.
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.");
    }
}
