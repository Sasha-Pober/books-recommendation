using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Ratings;
using BookStore.Services.Ratings;
using BookStore.UnitTests.Shopping;
using NSubstitute;

namespace BookStore.UnitTests.Ratings;

public class RatingServiceTests : ShoppingTestBase
{
    private readonly IRatingRepository _ratings = Substitute.For<IRatingRepository>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly Guid _bookId = Guid.NewGuid();

    public RatingServiceTests() => Books.ExistsAsync(_bookId, Arg.Any<CancellationToken>()).Returns(true);

    [Theory]
    [InlineData(5, 1.0)]
    [InlineData(2, 0.0)]
    public async Task Rate_CreatesRating_AndRecordsWeightedRatingEvent(int score, double weight)
    {
        await CreateService().RateAsync(_bookId, new RateBookRequest { Score = score, ReviewText = "  " }, CancellationToken.None);

        _ratings.Received(1).Add(Arg.Is<Rating>(r => r.Score == score && r.UserId == UserId && r.ReviewText == null));
        var recorded = Assert.Single(RecordedInteractions);
        Assert.Equal(InteractionType.Rating, recorded.EventType);
        Assert.Equal(weight, recorded.Weight);
    }

    [Fact]
    public async Task Rate_Again_ReplacesTheRating()
    {
        var existing = new Rating { UserId = UserId, BookId = _bookId, Score = 2, ReviewText = "meh" };
        _ratings.GetAsync(UserId, _bookId, Arg.Any<CancellationToken>()).Returns(existing);

        await CreateService().RateAsync(_bookId, new RateBookRequest { Score = 5, ReviewText = "Чудово!" }, CancellationToken.None);

        Assert.Equal(5, existing.Score);
        Assert.Equal("Чудово!", existing.ReviewText);
        _ratings.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Rate_AsGuest_ThrowsUnauthorized()
    {
        CurrentUser.UserId.Returns((Guid?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService().RateAsync(_bookId, new RateBookRequest { Score = 5 }, CancellationToken.None));
    }

    private RatingService CreateService() =>
        new(_ratings, Books, Users, Interactions, _identity, CurrentUser, UnitOfWork);
}
