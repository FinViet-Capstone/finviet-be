using System.Threading.Channels;

namespace FinViet.Infrastructure.Services.Background;

/// <summary>One customer's slice of newly imported SePay expenses waiting for categorization.</summary>
public sealed record SepayCategorizationBatch(Guid CustomerId, IReadOnlyList<Guid> TransactionIds);

/// <summary>
/// In-memory hand-off between the SePay import paths and <see cref="SepayCategorizationWorker"/>.
/// The channel is unbounded because it only holds ids. A restart drops whatever is queued; those
/// rows stop counting as pending after <c>TransactionCategorization.PendingWindow</c> and surface
/// as failed so the user can retry.
/// </summary>
public interface ISepayCategorizationQueue
{
    /// <summary>Queues ids for background categorization, split into batches of at most <see cref="SepayCategorizationQueue.MaxBatchSize"/>.</summary>
    void Enqueue(Guid customerId, IReadOnlyCollection<Guid> transactionIds);
}

public sealed class SepayCategorizationQueue : ISepayCategorizationQueue
{
    public const int MaxBatchSize = 20;

    private readonly Channel<SepayCategorizationBatch> _channel =
        Channel.CreateUnbounded<SepayCategorizationBatch>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<SepayCategorizationBatch> Reader => _channel.Reader;

    public void Enqueue(Guid customerId, IReadOnlyCollection<Guid> transactionIds)
    {
        foreach (var chunk in transactionIds.Chunk(MaxBatchSize))
            _channel.Writer.TryWrite(new SepayCategorizationBatch(customerId, chunk));
    }
}
