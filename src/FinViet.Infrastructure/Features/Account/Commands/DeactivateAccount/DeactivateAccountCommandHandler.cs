using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Account.Commands.DeactivateAccount;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Account.Commands.DeactivateAccount;

public class DeactivateAccountCommandHandler : IRequestHandler<DeactivateAccountCommand, string>
{
    private readonly FinVietDbContext _db;
    public DeactivateAccountCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<string> Handle(DeactivateAccountCommand request, CancellationToken cancellationToken)
    {
        var c = await _db.Customers
            .FirstOrDefaultAsync(x => x.CustomerId == request.TargetCustomerId, cancellationToken);

        if (c is null) throw new NotFoundException("Customer", request.TargetCustomerId);

        c.IsActive = false;
        c.Status   = "INACTIVE";

        var tokens = await _db.RefreshTokens
            .Where(t => t.CustomerId == request.TargetCustomerId && !t.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var t in tokens) { t.IsRevoked = true; t.RevokedAt = DateTime.UtcNow; }

        await _db.SaveChangesAsync(cancellationToken);
        return $"Account {request.TargetCustomerId} has been deactivated.";
    }
}
