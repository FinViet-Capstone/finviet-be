namespace FinViet.Application.Interfaces;

public interface IBudgetAlertNotifier
{
    Task SendBudgetAlertAsync(
        Guid customerId,
        string title,
        string message,
        CancellationToken cancellationToken = default);
}
