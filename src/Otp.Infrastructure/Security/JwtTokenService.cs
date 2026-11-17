using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Otp.Application.Abstractions;
using Otp.Domain.Entities;

namespace Otp.Infrastructure.Security;

public class JwtOptions
{
    public string Issuer { get; set; } = "otp-mfa-auth";
    public string Audience { get; set; } = "otp-mfa-web";
    /// <summary>HMAC key, at least 32 bytes. Supply via user-secrets / env, never commit.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public TimeSpan AccessLifetime { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan MfaLifetime { get; set; } = TimeSpan.FromMinutes(5);

    public SymmetricSecurityKey Key => new(Encoding.UTF8.GetBytes(SigningKey));
}

public static class TokenPurpose
{
    public const string Claim = "purpose";
    public const string Access = "access";
    public const string Mfa = "mfa";
}

public class JwtTokenService(IOptions<JwtOptions> options, TimeProvider time) : ITokenService
{
    private readonly JwtOptions _o = options.Value;

    public string CreateAccessToken(User user) => Create(user, TokenPurpose.Access, _o.AccessLifetime);
    public string CreateMfaToken(User user) => Create(user, TokenPurpose.Mfa, _o.MfaLifetime);

    private string Create(User user, string purpose, TimeSpan lifetime)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _o.Issuer,
            Audience = _o.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
                [TokenPurpose.Claim] = purpose,
            },
            SigningCredentials = new SigningCredentials(_o.Key, SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
