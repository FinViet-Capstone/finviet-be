using FinViet.Application.DTOs.Auth;
using MediatR;

namespace FinViet.Application.Features.Auth.Commands.RefreshToken;

public record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResponseDto>;
