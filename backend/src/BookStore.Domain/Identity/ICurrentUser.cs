namespace BookStore.Domain.Identity;

/// <summary>The caller of the current request.</summary>
public interface ICurrentUser
{
    /// <summary>Null for guests.</summary>
    Guid? UserId { get; }

    /// <summary>
    /// Browser session id (from a cookie), present for guests and signed-in users alike.
    /// Guest events are linked to it and attached to the account on sign-in.
    /// </summary>
    string SessionId { get; }
}
