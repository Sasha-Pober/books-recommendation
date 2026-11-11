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

        _queue.Received(1).TryEnqueue(Arg.Is<IReadOnlyCollection<QueuedView>>(views =>
            views.Count == 2
            && views.All(v => v.Interaction.UserId == userId
                              && v.Interaction.SessionId == "session-1"
                              && v.Interaction.Weight == 0.2)));
    }

    [Fact]
    public void Record_PassesTheClickedRecommendationLogId()
    {
        _currentUser.SessionId.Returns("session-1");
        _queue.TryEnqueue(default!).ReturnsForAnyArgs(true);
        var request = new RecordInteractionsRequest
        {
            Events = [new InteractionEvent { BookId = Guid.NewGuid(), Type = InteractionType.View, RecommendationLogId = 42 }],
        };

        CreateService().Record(request);

        _queue.Received(1).TryEnqueue(Arg.Is<IReadOnlyCollection<QueuedView>>(views =>
            views.Single().RecommendationLogId == 42));
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
