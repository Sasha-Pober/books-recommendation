using BookStore.Domain.Entities;
using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;
using NSubstitute;

namespace BookStore.UnitTests.Shopping;

/// <summary>A signed-in customer with mocked repositories.</summary>
public abstract class ShoppingTestBase
{
    protected const string SessionId = "session-1";

    protected readonly Guid UserId = Guid.NewGuid();
    protected readonly IBookRepository Books = Substitute.For<IBookRepository>();
    protected readonly IUserRepository Users = Substitute.For<IUserRepository>();
    protected readonly IInteractionRepository Interactions = Substitute.For<IInteractionRepository>();
    protected readonly ICurrentUser CurrentUser = Substitute.For<ICurrentUser>();
    protected readonly IUnitOfWork UnitOfWork = Substitute.For<IUnitOfWork>();

    /// <summary>Every interaction passed to <see cref="IInteractionRepository.AddRange"/>.</summary>
    protected readonly List<Interaction> RecordedInteractions = [];

    protected ShoppingTestBase()
    {
        CurrentUser.UserId.Returns(UserId);
        CurrentUser.SessionId.Returns(SessionId);
        Users.ExistsAsync(UserId, Arg.Any<CancellationToken>()).Returns(true);
        Interactions.When(i => i.AddRange(Arg.Any<IEnumerable<Interaction>>()))
            .Do(call => RecordedInteractions.AddRange(call.Arg<IEnumerable<Interaction>>()));
    }

    protected static Book NewBook(string title, decimal price = 100, int stock = 10) => new()
    {
        Title = title,
        Price = price,
        Stock = stock,
        Authors = [new Author { FullName = "Author" }],
    };
}
