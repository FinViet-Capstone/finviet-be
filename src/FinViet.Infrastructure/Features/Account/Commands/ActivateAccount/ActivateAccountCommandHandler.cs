using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Account.Commands.ActivateAccount;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Account.Commands.ActivateAccount;

public class ActivateAccountCommandHandler : IRequestHandler<ActivateAccountCommand, string>
{
    private readonly FinVietDbContext _db;
    public ActivateAccountCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<string> Handle(ActivateAccountCommand request, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(
            x => x.CustomerId == request.TargetCustomerId && x.DeletedAt == null,
            cancellationToken);

        if (customer is null)
            throw new NotFoundException("Customer", request.TargetCustomerId);

        customer.IsActive = true;
        await _db.SaveChangesAsync(cancellationToken);

        return $"Account {request.TargetCustomerId} has been activated.";
    }
}
