using BookStore.Domain.Enums;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Interactions;
using BookStore.Services.Interactions;
using NSubstitute;

namespace BookStore.UnitTests.Services;

public class InteractionServiceTests
{
    [Theory]
    [InlineData(InteractionType.AddToCart)]
    [InlineData(InteractionType.Purchase)]
    [InlineData(InteractionType.Rating)]
    public async Task Record_RejectsEventsTheServerRecordsItself(InteractionType type)
    {
        var interactions = Substitute.For<IInteractionRepository>();
        var service = new InteractionService(
            Substitute.For<IBookRepository>(),
            Substitute.For<IUserRepository>(),
            interactions,
            Substitute.For<ICurrentUser>(),
            Substitute.For<IUnitOfWork>());

        var request = new RecordInteractionsRequest { Events = [new InteractionEvent { BookId = Guid.NewGuid(), Type = type }] };

        await Assert.ThrowsAsync<ValidationException>(() => service.RecordAsync(request, CancellationToken.None));
        interactions.DidNotReceiveWithAnyArgs().AddRange(default!);
    }
}
