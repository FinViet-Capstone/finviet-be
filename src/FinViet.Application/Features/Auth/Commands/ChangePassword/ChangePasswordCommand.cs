using MediatR;

namespace FinViet.Application.Features.Auth.Commands.ChangePassword;

public record ChangePasswordCommand(
    Guid CustomerId,
    string CurrentPassword,
    string NewPassword
) : IRequest<string>;
