using MediatR;

namespace FinViet.Application.Features.Account.Commands.DeactivateAccount;

/// <summary>Admin deactivates a customer account.</summary>
public record DeactivateAccountCommand(Guid TargetCustomerId) : IRequest<string>;
