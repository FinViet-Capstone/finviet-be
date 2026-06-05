using MediatR;

namespace FinViet.Application.Features.Auth.Commands.ResetPassword;

public record ResetPasswordCommand(
    string Token,
    string NewPassword,
    string ConfirmPassword
) : IRequest<string>;
