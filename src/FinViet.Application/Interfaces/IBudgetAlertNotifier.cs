namespace FinViet.Application.Interfaces;

public interface IBudgetAlertNotifier
{
    Task SendBudgetAlertAsync(
        Guid customerId,
        Guid budgetId,
        string title,
        string message,
        CancellationToken cancellationToken = default);
}
