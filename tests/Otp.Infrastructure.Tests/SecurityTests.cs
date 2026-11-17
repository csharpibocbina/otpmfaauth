using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Otp.Domain.Entities;
using Otp.Infrastructure.Security;

namespace Otp.Infrastructure.Tests;

public class Argon2PasswordHasherTests
{
    private readonly Argon2PasswordHasher _sut = new();

    [Fact]
    public void Hash_ThenVerify_Succeeds_AndSaltsDiffer()
    {
        var h1 = _sut.Hash("correct horse battery");
        var h2 = _sut.Hash("correct horse battery");
        h1.Should().NotBe(h2);
        _sut.Verify("correct horse battery", h1).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_Fails() =>
        _sut.Verify("nope", _sut.Hash("correct horse battery")).Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("v1$!!$!!")]
    public void Verify_MalformedHash_ReturnsFalseInsteadOfThrowing(string hash) =>
        _sut.Verify("x", hash).Should().BeFalse();
}

public class JwtTokenServiceTests
{
    private readonly JwtOptions _opts = new() { SigningKey = new string('k', 48) };
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly User _user = new() { Id = Guid.NewGuid(), Email = "a@b.com" };

    private async Task<TokenValidationResult> Validate(string token) =>
        await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = _opts.Issuer, ValidAudience = _opts.Audience, IssuerSigningKey = _opts.Key,
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (_, exp, _, _) => exp > _time.GetUtcNow().UtcDateTime,
        });

    [Fact]
    public async Task AccessToken_HasAccessPurpose_AndSubject()
    {
        var token = new JwtTokenService(Options.Create(_opts), _time).CreateAccessToken(_user);
        var r = await Validate(token);
        r.IsValid.Should().BeTrue();
        r.Claims[TokenPurpose.Claim].Should().Be(TokenPurpose.Access);
        r.Claims["sub"].Should().Be(_user.Id.ToString());
    }

    [Fact]
    public async Task MfaToken_IsMarkedMfa_SoItCannotPassAccessPolicy()
    {
        var token = new JwtTokenService(Options.Create(_opts), _time).CreateMfaToken(_user);
        (await Validate(token)).Claims[TokenPurpose.Claim].Should().Be(TokenPurpose.Mfa);
    }

    [Fact]
    public async Task MfaToken_ExpiresAfterFiveMinutes()
    {
        var token = new JwtTokenService(Options.Create(_opts), _time).CreateMfaToken(_user);
        _time.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
        (await Validate(token)).IsValid.Should().BeFalse();
    }
}
