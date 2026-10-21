using BookStore.Domain.Entities;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Interactions;

namespace BookStore.Services.Interactions;

public interface IInteractionService
{
    Task RecordAsync(RecordInteractionsRequest request, CancellationToken cancellationToken);
}

internal sealed class InteractionService(
    IBookRepository books,
    IUserRepository users,
    IInteractionRepository interactions,
    IUnitOfWork unitOfWork) : IInteractionService
{
    public async Task RecordAsync(RecordInteractionsRequest request, CancellationToken cancellationToken)
    {
        var bookIds = request.Events.Select(e => e.BookId).Distinct().ToList();
        var unknownBooks = bookIds.Except(await books.GetExistingIdsAsync(bookIds, cancellationToken)).ToList();
        if (unknownBooks.Count > 0)
        {
            throw new ValidationException($"Unknown book ids: {string.Join(", ", unknownBooks)}.");
        }

        var userIds = request.Events.Where(e => e.UserId.HasValue).Select(e => e.UserId!.Value).Distinct().ToList();
        var unknownUsers = userIds.Except(await users.GetExistingIdsAsync(userIds, cancellationToken)).ToList();
        if (unknownUsers.Count > 0)
        {
            throw new ValidationException($"Unknown user ids: {string.Join(", ", unknownUsers)}.");
        }

        interactions.AddRange(request.Events.Select(e => new Interaction
        {
            UserId = e.UserId,
            SessionId = e.SessionId,
            BookId = e.BookId,
            Type = e.Type,
            RecommendationId = e.RecommendationId,
        }));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
