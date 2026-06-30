using FinViet.Application.DTOs.Auth;
using MediatR;

namespace FinViet.Application.Features.Auth.Commands.AdminLogin;

public record AdminLoginCommand(
    string Username,
    string Password
) : IRequest<AuthResponseDto>;