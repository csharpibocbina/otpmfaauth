using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Otp.Application;
using Otp.Application.Abstractions;
using Otp.Application.Common;
using Otp.Application.Services;
using Otp.Domain.Entities;
using Otp.Domain.Events;

namespace Otp.Application.Tests;

public class MfaServiceTests
{
    private readonly IUserRepository _users = A.Fake<IUserRepository>();
    private readonly IVerificationRepository _attempts = A.Fake<IVerificationRepository>();
    private readonly IEventBus _bus = A.Fake<IEventBus>();
    private readonly IVonageVerifyClient _vonage = A.Fake<IVonageVerifyClient>();
    private readonly ITokenService _tokens = A.Fake<ITokenService>();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    private readonly MfaService _sut;
    private readonly User _user = new() { Id = Guid.NewGuid(), PhoneE164 = "+14155550100" };

    public MfaServiceTests()
    {
        _sut = new MfaService(_users, _attempts, _bus, _vonage, _tokens, _time,
            Options.Create(new MfaOptions()), NullLogger<MfaService>.Instance);
        A.CallTo(() => _users.GetByIdAsync(_user.Id, A<CancellationToken>._)).Returns(_user);
        A.CallTo(() => _tokens.CreateAccessToken(_user)).Returns("access");
    }

    private VerificationAttempt GivenSentAttempt(int checks = 0, AttemptStatus status = AttemptStatus.Sent)
    {
        var a = new VerificationAttempt
        {
            UserId = _user.Id, Status = status, VonageRequestId = "req-1", CheckCount = checks,
            ExpiresAt = _time.GetUtcNow().AddMinutes(5),
        };
        A.CallTo(() => _attempts.GetAsync(a.Id, A<CancellationToken>._)).Returns(a);
        return a;
    }

    // ---- StartChallenge ----

    [Fact]
    public async Task StartChallenge_ValidUser_SavesAttemptPublishesEventAndDoesNotCallVonage()
    {
        var r = await _sut.StartChallengeAsync(_user.Id, "corr", default);

        r.IsSuccess.Should().BeTrue();
        r.Value!.MaskedDestination.Should().NotContain("4155550");
        A.CallTo(() => _attempts.AddAsync(A<VerificationAttempt>.That.Matches(v =>
            v.UserId == _user.Id && v.Status == AttemptStatus.Pending), A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _bus.PublishAsync(A<OtpRequested>.That.Matches(e =>
            e.ChallengeId == r.Value.ChallengeId && e.Destination == "+14155550100"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _vonage.StartAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task StartChallenge_UserWithoutPhone_Fails()
    {
        _user.PhoneE164 = null;
        var r = await _sut.StartChallengeAsync(_user.Id, "c", default);
        r.Error.Should().Be(ErrorCode.PhoneNotVerified);
        A.CallTo(() => _bus.PublishAsync(A<OtpRequested>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task StartChallenge_WithinCooldown_IsRateLimited()
    {
        A.CallTo(() => _attempts.GetLastCreatedAsync(_user.Id, A<CancellationToken>._))
            .Returns(_time.GetUtcNow().AddSeconds(-10));
        var r = await _sut.StartChallengeAsync(_user.Id, "c", default);
        r.Error.Should().Be(ErrorCode.TooManyRequests);
    }

    [Fact]
    public async Task StartChallenge_OverWindowLimit_IsRateLimited()
    {
        A.CallTo(() => _attempts.CountSinceAsync(_user.Id, A<DateTimeOffset>._, A<CancellationToken>._)).Returns(5);
        var r = await _sut.StartChallengeAsync(_user.Id, "c", default);
        r.Error.Should().Be(ErrorCode.TooManyRequests);
        A.CallTo(() => _attempts.AddAsync(A<VerificationAttempt>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    // ---- Verify ----

    [Fact]
    public async Task Verify_CorrectCode_ApprovesMarksPhoneVerifiedAndReturnsAccessToken()
    {
        var a = GivenSentAttempt();
        A.CallTo(() => _vonage.CheckAsync("req-1", "123456", A<CancellationToken>._)).Returns(VerifyCheckStatus.Approved);

        var r = await _sut.VerifyAsync(_user.Id, a.Id, "123456", "c", default);

        r.Value!.AccessToken.Should().Be("access");
        a.Status.Should().Be(AttemptStatus.Approved);
        _user.PhoneVerifiedAt.Should().Be(_time.GetUtcNow());
        A.CallTo(() => _bus.PublishAsync(A<MfaVerified>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Verify_WrongCode_IncrementsCountAndPublishesFailure()
    {
        var a = GivenSentAttempt(checks: 1);
        A.CallTo(() => _vonage.CheckAsync(A<string>._, A<string>._, A<CancellationToken>._)).Returns(VerifyCheckStatus.InvalidCode);

        var r = await _sut.VerifyAsync(_user.Id, a.Id, "000000", "c", default);

        r.Error.Should().Be(ErrorCode.InvalidCode);
        a.CheckCount.Should().Be(2);
        a.Status.Should().Be(AttemptStatus.Sent);
        A.CallTo(() => _bus.PublishAsync(A<MfaFailed>.That.Matches(f => f.Reason == "invalid_code"), A<CancellationToken>._)).MustHaveHappened();
    }

    [Fact]
    public async Task Verify_FifthWrongCode_LocksChallengeAndCancelsAtVonage()
    {
        var a = GivenSentAttempt(checks: 4);
        A.CallTo(() => _vonage.CheckAsync(A<string>._, A<string>._, A<CancellationToken>._)).Returns(VerifyCheckStatus.InvalidCode);

        var r = await _sut.VerifyAsync(_user.Id, a.Id, "000000", "c", default);

        r.Error.Should().Be(ErrorCode.ChallengeLocked);
        a.Status.Should().Be(AttemptStatus.Locked);
        A.CallTo(() => _vonage.CancelAsync("req-1", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Verify_LockedChallenge_DoesNotCallVonage()
    {
        var a = GivenSentAttempt(status: AttemptStatus.Locked);
        var r = await _sut.VerifyAsync(_user.Id, a.Id, "123456", "c", default);
        r.Error.Should().Be(ErrorCode.ChallengeLocked);
        A.CallTo(() => _vonage.CheckAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Verify_ExpiredChallenge_MarksExpiredWithoutCallingVonage()
    {
        var a = GivenSentAttempt();
        _time.Advance(TimeSpan.FromMinutes(6));

        var r = await _sut.VerifyAsync(_user.Id, a.Id, "123456", "c", default);

        r.Error.Should().Be(ErrorCode.ChallengeExpired);
        a.Status.Should().Be(AttemptStatus.Expired);
        A.CallTo(() => _vonage.CheckAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Verify_ChallengeOfAnotherUser_ReturnsNotFound()
    {
        var a = GivenSentAttempt();
        var r = await _sut.VerifyAsync(Guid.NewGuid(), a.Id, "123456", "c", default);
        r.Error.Should().Be(ErrorCode.NotFound);
        A.CallTo(() => _vonage.CheckAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Verify_PendingChallenge_IsNotAcceptedYet()
    {
        var a = GivenSentAttempt(status: AttemptStatus.Pending);
        var r = await _sut.VerifyAsync(_user.Id, a.Id, "123456", "c", default);
        r.Error.Should().Be(ErrorCode.NotFound);
    }

    [Fact]
    public async Task Verify_ProviderError_DoesNotCountAsUserAttempt()
    {
        var a = GivenSentAttempt(checks: 2);
        A.CallTo(() => _vonage.CheckAsync(A<string>._, A<string>._, A<CancellationToken>._)).Returns(VerifyCheckStatus.Error);

        var r = await _sut.VerifyAsync(_user.Id, a.Id, "123456", "c", default);

        r.Error.Should().Be(ErrorCode.ProviderError);
        a.CheckCount.Should().Be(2);
    }
}
