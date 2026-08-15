using MediatR;

namespace FinViet.Application.Features.Auth.Commands.ChangeAdminPassword;

public record ChangeAdminPasswordCommand(
    Guid AdminId,
    string CurrentPassword,
    string NewPassword
) : IRequest<string>;
