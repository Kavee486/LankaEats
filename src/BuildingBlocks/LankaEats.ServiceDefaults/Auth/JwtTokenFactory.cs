using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LankaEats.ServiceDefaults.Auth;

/// <summary>
/// Issues HMAC-signed JWTs. The gateway uses it for the demo login and the tests use it for
/// authenticated requests. A production deployment would use an identity provider such as
/// Keycloak or Entra ID instead.
/// </summary>
public sealed class JwtTokenFactory(JwtOptions options, TimeProvider clock)
{
    public string CreateToken(string userId, string displayName, string role)
    {
        var now = clock.GetUtcNow();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(options.TokenLifetimeMinutes).UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateKey(options), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [LankaEatsClaims.Subject] = userId,
                [LankaEatsClaims.Name] = displayName,
                [LankaEatsClaims.Role] = role,
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    internal static SymmetricSecurityKey CreateKey(JwtOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey));
}
