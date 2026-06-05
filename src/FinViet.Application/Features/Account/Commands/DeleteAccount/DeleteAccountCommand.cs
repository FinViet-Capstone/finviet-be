using MediatR;

namespace FinViet.Application.Features.Account.Commands.DeleteAccount;

public record DeleteAccountCommand(Guid CustomerId) : IRequest<string>;
