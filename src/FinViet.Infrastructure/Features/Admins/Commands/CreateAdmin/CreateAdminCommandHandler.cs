using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Admins;
using FinViet.Application.Features.Admins.Commands.CreateAdmin;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Admins.Commands.CreateAdmin;

public class CreateAdminCommandHandler : IRequestHandler<CreateAdminCommand, AdminResponse>
{
    private readonly FinVietDbContext _db;
    public CreateAdminCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<AdminResponse> Handle(CreateAdminCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.ToLower().Trim();
        var username = request.Username.Trim();

        var exists = await _db.Admins
            .AnyAsync(a => a.Email == normalizedEmail || a.Username == username, cancellationToken);

        if (exists)
            throw new ConflictException($"An admin with username '{username}' or email '{normalizedEmail}' already exists.");

        var admin = new Admin
        {
            AdminId      = Guid.NewGuid(),
            Username     = username,
            Email        = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CreatedAt    = DateTime.UtcNow
        };

        _db.Admins.Add(admin);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ConflictException($"An admin with username '{username}' or email '{normalizedEmail}' already exists.");
        }

        return new AdminResponse(admin.AdminId, admin.Username, admin.Email, admin.CreatedAt);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException?.Message ?? string.Empty;
        // Npgsql error code 23505 = unique_violation
        return inner.Contains("23505") || inner.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}
