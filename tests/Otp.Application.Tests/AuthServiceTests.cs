using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Otp.Application;
using Otp.Application.Abstractions;
using Otp.Application.Common;
using Otp.Application.Services;
using Otp.Domain.Entities;

namespace Otp.Application.Tests;

public class AuthServiceTests
{
    private readonly IUserRepository _users = A.Fake<IUserRepository>();
    private readonly IPasswordHasher _hasher = A.Fake<IPasswordHasher>();
    private readonly ITokenService _tokens = A.Fake<ITokenService>();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _sut = new AuthService(_users, _hasher, _tokens, _time, Options.Create(new LockoutOptions()));
        A.CallTo(() => _tokens.CreateAccessToken(A<User>._)).Returns("access");
        A.CallTo(() => _tokens.CreateMfaToken(A<User>._)).Returns("mfa");
    }

    private User GivenUser(bool mfa = false, int failed = 0)
    {
        var u = new User { Email = "a@b.com", PasswordHash = "h", MfaEnabled = mfa, FailedLoginCount = failed };
        A.CallTo(() => _users.GetByEmailAsync("a@b.com", A<CancellationToken>._)).Returns(u);
        return u;
    }

    [Fact]
    public async Task Login_UnknownUser_ReturnsInvalidCredentials()
    {
        A.CallTo(() => _users.GetByEmailAsync(A<string>._, A<CancellationToken>._)).Returns((User?)null);
        var r = await _sut.LoginAsync("x@y.com", "pw", default);
        r.Error.Should().Be(ErrorCode.InvalidCredentials);
    }

    [Fact]
    public async Task Login_WrongPassword_IncrementsFailureCount()
    {
        var u = GivenUser();
        A.CallTo(() => _hasher.Verify("bad", "h")).Returns(false);

        var r = await _sut.LoginAsync("a@b.com", "bad", default);

        r.Error.Should().Be(ErrorCode.InvalidCredentials);
        u.FailedLoginCount.Should().Be(1);
        A.CallTo(() => _users.SaveChangesAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Login_FifthWrongPassword_LocksAccount()
    {
        var u = GivenUser(failed: 4);
        A.CallTo(() => _hasher.Verify(A<string>._, A<string>._)).Returns(false);

        await _sut.LoginAsync("a@b.com", "bad", default);

        u.LockedUntil.Should().Be(_time.GetUtcNow().AddMinutes(15));
    }

    [Fact]
    public async Task Login_LockedAccount_IsRejectedWithoutCheckingPassword()
    {
        var u = GivenUser();
        u.LockedUntil = _time.GetUtcNow().AddMinutes(1);

        var r = await _sut.LoginAsync("a@b.com", "pw", default);

        r.Error.Should().Be(ErrorCode.AccountLocked);
        A.CallTo(() => _hasher.Verify(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Login_MfaEnabled_ReturnsMfaTokenNotAccessToken()
    {
        GivenUser(mfa: true, failed: 2);
        A.CallTo(() => _hasher.Verify("pw", "h")).Returns(true);

        var r = await _sut.LoginAsync("a@b.com", "pw", default);

        r.Value!.Status.Should().Be(LoginStatus.MfaRequired);
        r.Value.MfaToken.Should().Be("mfa");
        r.Value.AccessToken.Should().BeNull();
    }

    [Fact]
    public async Task Login_NoMfa_ReturnsAccessTokenAndResetsFailures()
    {
        var u = GivenUser(failed: 2);
        A.CallTo(() => _hasher.Verify("pw", "h")).Returns(true);

        var r = await _sut.LoginAsync("a@b.com", "pw", default);

        r.Value!.AccessToken.Should().Be("access");
        u.FailedLoginCount.Should().Be(0);
    }

    [Theory]
    [InlineData("not-an-email", "longenoughpassword", null)]
    [InlineData("a@b.com", "short", null)]
    [InlineData("a@b.com", "longenoughpassword", "0123")]
    public async Task Register_InvalidInput_ReturnsValidationError(string email, string pw, string? phone)
    {
        var r = await _sut.RegisterAsync(email, pw, phone, default);
        r.Error.Should().Be(ErrorCode.Validation);
        A.CallTo(() => _users.AddAsync(A<User>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Register_ExistingEmail_ReturnsEmailTaken()
    {
        GivenUser();
        var r = await _sut.RegisterAsync("A@B.com", "longenoughpassword", null, default);
        r.Error.Should().Be(ErrorCode.EmailTaken);
    }

    [Fact]
    public async Task Register_WithPhone_StoresHashedPasswordAndEnablesMfa()
    {
        A.CallTo(() => _users.GetByEmailAsync(A<string>._, A<CancellationToken>._)).Returns((User?)null);
        A.CallTo(() => _hasher.Hash("longenoughpassword")).Returns("HASHED");

        var r = await _sut.RegisterAsync("a@b.com", "longenoughpassword", "+14155550100", default);

        r.IsSuccess.Should().BeTrue();
        A.CallTo(() => _users.AddAsync(A<User>.That.Matches(u =>
            u.PasswordHash == "HASHED" && u.MfaEnabled && u.Email == "a@b.com"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }
}
