using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Interactions;

namespace BookStore.Services.Interactions;

public interface IInteractionService
{
    /// <summary>Records events of the current user (or guest session).</summary>
    Task RecordAsync(RecordInteractionsRequest request, CancellationToken cancellationToken);
}

internal sealed class InteractionService(
    IBookRepository books,
    IUserRepository users,
    IInteractionRepository interactions,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork) : IInteractionService
{
    public async Task RecordAsync(RecordInteractionsRequest request, CancellationToken cancellationToken)
    {
        if (request.Events.Any(e => e.Type == InteractionType.Rating))
        {
            throw new ValidationException("Ratings are submitted through the book rating endpoint.");
        }

        var bookIds = request.Events.Select(e => e.BookId).Distinct().ToList();
        var unknownBooks = bookIds.Except(await books.GetExistingIdsAsync(bookIds, cancellationToken)).ToList();
        if (unknownBooks.Count > 0)
        {
            throw new ValidationException($"Unknown book ids: {string.Join(", ", unknownBooks)}.");
        }

        var userId = currentUser.UserId;
        if (userId is { } id && !await users.ExistsAsync(id, cancellationToken))
        {
            // Valid token of a deleted account.
            throw new UnauthorizedException("The user account no longer exists.");
        }

        interactions.AddRange(request.Events.Select(e =>
            Interaction.Create(userId, currentUser.SessionId, e.BookId, e.Type)));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
