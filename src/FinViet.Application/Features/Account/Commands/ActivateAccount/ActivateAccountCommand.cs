using MediatR;

namespace FinViet.Application.Features.Account.Commands.ActivateAccount;

/// <summary>Admin reactivates a customer account that was previously deactivated.</summary>
public record ActivateAccountCommand(Guid TargetCustomerId) : IRequest<string>;
