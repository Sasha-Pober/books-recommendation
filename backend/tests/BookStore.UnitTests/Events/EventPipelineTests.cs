using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Services;
using BookStore.Infrastructure.Events;
using Microsoft.Extensions.Options;

namespace BookStore.UnitTests.Events;

/// <summary>The queue alone; writing to the database is covered by the integration tests.</summary>
public class EventPipelineTests
{
    [Fact]
    public void Queue_RejectsWholeRequest_WhenItDoesNotFit()
    {
        var queue = new ChannelInteractionQueue(Options.Create(new EventQueueOptions { Capacity = 3 }));

        Assert.True(queue.TryEnqueue(Views(2)));
        Assert.False(queue.TryEnqueue(Views(2)));
        Assert.Equal(2, queue.Reader.Count);
        Assert.True(queue.TryEnqueue(Views(1)));
    }

    private static List<QueuedView> Views(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => new QueuedView(Interaction.Create(null, "s", Guid.NewGuid(), InteractionType.View)))
            .ToList();
}
