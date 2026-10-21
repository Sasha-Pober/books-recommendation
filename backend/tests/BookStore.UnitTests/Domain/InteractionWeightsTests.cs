using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Domain.Enums;

namespace BookStore.UnitTests.Domain;

public class InteractionWeightsTests
{
    [Theory]
    [InlineData(InteractionType.View, null, 0.2)]
    [InlineData(InteractionType.AddToWishlist, null, 0.5)]
    [InlineData(InteractionType.AddToCart, null, 0.7)]
    [InlineData(InteractionType.Purchase, null, 1.0)]
    [InlineData(InteractionType.Rating, 5, 1.0)]
    [InlineData(InteractionType.Rating, 4, 1.0)]
    [InlineData(InteractionType.Rating, 3, 0.0)]
    public void Create_AssignsWeightFromEventType(InteractionType type, int? score, double expected)
    {
        var interaction = Interaction.Create(null, "session", Guid.NewGuid(), type, score);

        Assert.Equal(expected, interaction.Weight);
    }

    [Fact]
    public void RatingWithoutScore_Throws() =>
        Assert.Throws<ArgumentNullException>(() => InteractionWeights.For(InteractionType.Rating));
}
