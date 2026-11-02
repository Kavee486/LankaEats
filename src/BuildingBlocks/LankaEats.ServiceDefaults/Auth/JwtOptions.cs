using System.ComponentModel.DataAnnotations;

namespace LankaEats.ServiceDefaults.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required] public string Issuer { get; init; } = "lankaeats";
    [Required] public string Audience { get; init; } = "lankaeats-api";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Supply it from a secret store outside development.</summary>
    [Required, MinLength(32)] public string SigningKey { get; init; } = string.Empty;

    public int TokenLifetimeMinutes { get; init; } = 120;
}

public static class LankaEatsClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Role = "role";
}

public static class LankaEatsRoles
{
    public const string Customer = "customer";
    public const string Admin = "admin";
}

public static class LankaEatsPolicies
{
    public const string Admin = "admin";
}
