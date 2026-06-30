namespace FinViet.Application.Interfaces;

/// <summary>Pushes a "weekly report ready" notification to a customer's device(s).</summary>
public interface IAiReportNotifier
{
    Task SendReportReadyAsync(
        Guid customerId,
        string title,
        string message,
        CancellationToken cancellationToken = default);
}
