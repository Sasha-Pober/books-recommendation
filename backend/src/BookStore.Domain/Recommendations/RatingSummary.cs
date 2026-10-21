namespace BookStore.Domain.Recommendations;

public sealed record RatingSummary(double? Average, int Count)
{
    public static readonly RatingSummary Empty = new(null, 0);
}
