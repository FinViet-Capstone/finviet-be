using MediatR;

namespace FinViet.Application.Features.Auth.Commands.ResendVerificationEmail;

public record ResendVerificationEmailCommand(string Email) : IRequest<string>;
