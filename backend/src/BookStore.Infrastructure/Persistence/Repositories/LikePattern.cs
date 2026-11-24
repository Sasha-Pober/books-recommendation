namespace BookStore.Infrastructure.Persistence.Repositories;

/// <summary>Substring patterns for ILIKE (answered by the trigram GIN indexes on titles and author names).</summary>
internal static class LikePattern
{
    public const string EscapeCharacter = "\\";

    /// <summary>"%value%" with %, _ and \ in the value matched literally.</summary>
    public static string Contains(string value) =>
        $"%{value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
}
