using System.Threading.Channels;

namespace FinViet.Infrastructure.Services.Background;

/// <summary>
/// Singleton in-process signal channel shared between <see cref="AiClassificationQueue"/> (scoped,
/// posts queue ids after persisting them) and <see cref="ClassificationQueueProcessor"/> (singleton,
/// drains them). The durable ai_classification_queue table is the source of truth; this channel is
/// only a fast-path wake-up so the processor doesn't have to poll tightly.
/// </summary>
public class ClassificationQueueSignal
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ValueTask SignalAsync(Guid queueId, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(queueId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
