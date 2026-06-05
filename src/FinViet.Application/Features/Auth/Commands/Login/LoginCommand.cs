using FinViet.Application.DTOs.Auth;
using MediatR;

namespace FinViet.Application.Features.Auth.Commands.Login;

public record LoginCommand(
    string Email,
    string Password
) : IRequest<AuthResponseDto>;
