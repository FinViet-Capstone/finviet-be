using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Account.Commands.DeleteAccount;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Account.Commands.DeleteAccount;

public class DeleteAccountCommandHandler : IRequestHandler<DeleteAccountCommand, string>
{
    private readonly FinVietDbContext _db;
    public DeleteAccountCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<string> Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
    {
        var c = await _db.Customers
            .FirstOrDefaultAsync(x => x.CustomerId == request.CustomerId && x.IsActive, cancellationToken);

        if (c is null) throw new NotFoundException("Customer", request.CustomerId);

        c.IsActive  = false;
        c.DeletedAt = DateTime.UtcNow;

        var tokens = await _db.RefreshTokens
            .Where(t => t.CustomerId == request.CustomerId && !t.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var t in tokens) { t.IsRevoked = true; t.RevokedAt = DateTime.UtcNow; }

        await _db.SaveChangesAsync(cancellationToken);
        return "Your account has been deleted.";
    }
}
