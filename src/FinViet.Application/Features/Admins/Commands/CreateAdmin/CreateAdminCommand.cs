using FinViet.Application.DTOs.Admins;
using MediatR;

namespace FinViet.Application.Features.Admins.Commands.CreateAdmin;

public record CreateAdminCommand(
    string Username,
    string Email,
    string Password
) : IRequest<AdminResponse>;
