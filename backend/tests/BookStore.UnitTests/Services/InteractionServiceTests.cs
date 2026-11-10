using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Services;
using BookStore.Presentation.Interactions;
using BookStore.Services.Interactions;
using NSubstitute;

namespace BookStore.UnitTests.Services;

public class InteractionServiceTests
{
    private readonly IInteractionQueue _queue = Substitute.For<IInteractionQueue>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    [Theory]
    [InlineData(InteractionType.AddToCart)]
    [InlineData(InteractionType.Purchase)]
    [InlineData(InteractionType.Rating)]
    public void Record_RejectsEventsTheServerRecordsItself(InteractionType type)
    {
        Assert.Throws<ValidationException>(() => CreateService().Record(Request(type)));

        _queue.DidNotReceiveWithAnyArgs().TryEnqueue(default!);
    }

    [Fact]
    public void Record_QueuesViewsOfTheCurrentUserAndSession()
    {
        var userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _currentUser.SessionId.Returns("session-1");
        _queue.TryEnqueue(default!).ReturnsForAnyArgs(true);

        CreateService().Record(Request(InteractionType.View, InteractionType.View));

        _queue.Received(1).TryEnqueue(Arg.Is<IReadOnlyCollection<Interaction>>(events =>
            events.Count == 2
            && events.All(e => e.UserId == userId && e.SessionId == "session-1" && e.Weight == 0.2)));
    }

    [Fact]
    public void Record_WhenQueueIsFull_ThrowsServiceUnavailable()
    {
        _queue.TryEnqueue(default!).ReturnsForAnyArgs(false);

        var error = Assert.Throws<ServiceUnavailableException>(() => CreateService().Record(Request(InteractionType.View)));
        Assert.True(error.RetryAfter > TimeSpan.Zero);
    }

    private InteractionService CreateService() => new(_queue, _currentUser);

    private static RecordInteractionsRequest Request(params InteractionType[] types) => new()
    {
        Events = types.Select(t => new InteractionEvent { BookId = Guid.NewGuid(), Type = t }).ToList(),
    };
}
