using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Common;
using BookStore.Presentation.Ratings;
using BookStore.Services.Common;

namespace BookStore.Services.Ratings;

public interface IRatingService
{
    /// <summary>Creates or replaces the current user's rating of the book and records a rating event.</summary>
    Task<ReviewDto> RateAsync(Guid bookId, RateBookRequest request, CancellationToken cancellationToken);

    Task<PagedResult<ReviewDto>> GetReviewsAsync(Guid bookId, ReviewQuery query, CancellationToken cancellationToken);

    /// <summary>The current user's rating of the book, or null if they haven't rated it.</summary>
    Task<ReviewDto?> GetMineAsync(Guid bookId, CancellationToken cancellationToken);
}

internal sealed class RatingService(
    IRatingRepository ratings,
    IBookRepository books,
    IUserRepository users,
    IInteractionRepository interactions,
    IIdentityService identity,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork) : IRatingService
{
    public async Task<ReviewDto> RateAsync(Guid bookId, RateBookRequest request, CancellationToken cancellationToken)
    {
        var userId = await currentUser.RequireUserIdAsync(users, cancellationToken);
        if (!await books.ExistsAsync(bookId, cancellationToken))
        {
            throw new NotFoundException(nameof(Book), bookId);
        }

        var reviewText = string.IsNullOrWhiteSpace(request.ReviewText) ? null : request.ReviewText.Trim();
        // The rating and the book's rating counters change together or not at all.
        var rating = await unitOfWork.InTransactionAsync(async ct =>
        {
            var saved = await ratings.GetAsync(userId, bookId, ct);
            int countDelta, sumDelta;
            if (saved is null)
            {
                saved = new Rating { BookId = bookId, UserId = userId, Score = request.Score, ReviewText = reviewText };
                ratings.Add(saved);
                (countDelta, sumDelta) = (1, request.Score);
            }
            else
            {
                (countDelta, sumDelta) = (0, request.Score - saved.Score);
                saved.Score = request.Score;
                saved.ReviewText = reviewText;
                saved.UpdatedAt = DateTime.UtcNow;
            }

            interactions.AddRange(
                [Interaction.Create(userId, currentUser.SessionId, bookId, InteractionType.Rating, request.Score)]);
            await unitOfWork.SaveChangesAsync(ct);
            if (countDelta != 0 || sumDelta != 0)
            {
                await books.AdjustRatingsAsync(bookId, countDelta, sumDelta, ct);
            }

            return saved;
        }, cancellationToken);

        var user = await identity.FindByIdAsync(userId, cancellationToken);
        return new ReviewDto(
            user?.FullName ?? "", rating.Score, rating.ReviewText, rating.CreatedAt, rating.UpdatedAt);
    }

    public async Task<PagedResult<ReviewDto>> GetReviewsAsync(
        Guid bookId, ReviewQuery query, CancellationToken cancellationToken)
    {
        if (!await books.ExistsAsync(bookId, cancellationToken))
        {
            throw new NotFoundException(nameof(Book), bookId);
        }

        var result = await ratings.GetReviewsAsync(
            bookId, query.Page, query.PageSize, query.WithTextOnly, cancellationToken);
        return new PagedResult<ReviewDto>(
            result.Items.Select(r => new ReviewDto(r.ReviewerName, r.Score, r.ReviewText, r.CreatedAt, r.UpdatedAt)).ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<ReviewDto?> GetMineAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var userId = await currentUser.RequireUserIdAsync(users, cancellationToken);
        var rating = await ratings.GetAsync(userId, bookId, cancellationToken);
        if (rating is null)
        {
            return null;
        }

        var user = await identity.FindByIdAsync(userId, cancellationToken);
        return new ReviewDto(user?.FullName ?? "", rating.Score, rating.ReviewText, rating.CreatedAt, rating.UpdatedAt);
    }
}
