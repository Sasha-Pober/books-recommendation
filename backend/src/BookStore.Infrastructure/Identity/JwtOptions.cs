using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BookStore.Infrastructure.Identity;

/// <summary>Configuration section "Jwt". Shared by token issuing (here) and validation (API).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "BookStore";

    [Required]
    public string Audience { get; set; } = "BookStore.Client";

    /// <summary>HMAC-SHA256 key; at least 32 characters. Keep it out of source control outside development.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = "";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}
