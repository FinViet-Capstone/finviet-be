using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services.Background;

namespace FinViet.Infrastructure.Services;

/// <summary>Persists a pending classification row and signals the background processor.</summary>
public class AiClassificationQueue : IAiClassificationQueue
{
    private readonly FinVietDbContext _db;
    private readonly ClassificationQueueSignal _signal;

    public AiClassificationQueue(FinVietDbContext db, ClassificationQueueSignal signal)
    {
        _db = db;
        _signal = signal;
    }

    public async Task EnqueueAsync(
        Guid transactionId,
        Guid customerId,
        string rawInput,
        CancellationToken cancellationToken = default)
    {
        var item = new AiClassificationQueueItem
        {
            QueueId = Guid.NewGuid(),
            TransactionId = transactionId,
            CustomerId = customerId,
            RawInput = rawInput,
            Status = "PENDING",
            AttemptCount = 0,
            EnqueuedAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow
        };

        _db.AiClassificationQueueItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);

        await _signal.SignalAsync(item.QueueId, cancellationToken);
    }
}
