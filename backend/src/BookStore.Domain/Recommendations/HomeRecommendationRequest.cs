namespace BookStore.Domain.Recommendations;

/// <param name="UserId">Null for guests.</param>
/// <param name="SessionId">Browser session; lets the engine build a profile from a guest's (or new user's) views.</param>
/// <param name="ExcludeBookIds">Books the user already bought or rated.</param>
public sealed record HomeRecommendationRequest(
    Guid? UserId, string SessionId, int Count, IReadOnlyCollection<Guid> ExcludeBookIds);
