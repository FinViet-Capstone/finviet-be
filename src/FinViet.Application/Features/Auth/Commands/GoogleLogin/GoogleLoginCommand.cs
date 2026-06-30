using FinViet.Application.DTOs.Auth;
using MediatR;

namespace FinViet.Application.Features.Auth.Commands.GoogleLogin;

/// <summary>
/// Firebase ID token received from client after Google Sign-In.
/// </summary>
public record GoogleLoginCommand(string IdToken) : IRequest<AuthResponseDto>;
