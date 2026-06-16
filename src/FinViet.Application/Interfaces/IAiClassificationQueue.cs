namespace FinViet.Application.Interfaces;

/// <summary>Durable fallback queue for transactions that need (re-)classification because Gemini
/// was unavailable. Writes are persisted to ai_classification_queue and signaled to an in-process
/// channel for prompt draining; the durable rows survive restarts.</summary>
public interface IAiClassificationQueue
{
    /// <summary>Persist a pending queue row and signal the processor.</summary>
    Task EnqueueAsync(
        Guid transactionId,
        Guid customerId,
        string rawInput,
        CancellationToken cancellationToken = default);
}
