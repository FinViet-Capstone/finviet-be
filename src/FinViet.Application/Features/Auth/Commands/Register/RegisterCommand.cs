using FinViet.Application.DTOs.Auth;
using MediatR;

namespace FinViet.Application.Features.Auth.Commands.Register;

public record RegisterCommand(
    string FullName,
    string Email,
    string Password
) : IRequest<string>; // returns "Registration successful, please verify your email."
