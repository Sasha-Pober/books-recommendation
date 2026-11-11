using System.Threading.Channels;
using BookStore.Domain.Services;
using Microsoft.Extensions.Options;

namespace BookStore.Infrastructure.Events;

/// <summary>In-process bounded queue; <see cref="InteractionBatchWriter"/> is its only reader.</summary>
internal sealed class ChannelInteractionQueue : IInteractionQueue
{
    private readonly Channel<QueuedView> _channel;
    private readonly int _capacity;

    public ChannelInteractionQueue(IOptions<EventQueueOptions> options)
    {
        _capacity = options.Value.Capacity;
        _channel = Channel.CreateBounded<QueuedView>(new BoundedChannelOptions(_capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    internal ChannelReader<QueuedView> Reader => _channel.Reader;

    public bool TryEnqueue(IReadOnlyCollection<QueuedView> views)
    {
        // Rejecting the whole request keeps client retries from duplicating a partially queued batch.
        // The check races with other writers, so a request may still be cut short in rare cases.
        if (_channel.Reader.Count + views.Count > _capacity)
        {
            return false;
        }

        foreach (var view in views)
        {
            if (!_channel.Writer.TryWrite(view))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Stops accepting events (on shutdown); queued events can still be read.</summary>
    internal void Complete() => _channel.Writer.TryComplete();
}
