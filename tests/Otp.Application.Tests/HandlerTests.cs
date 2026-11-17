using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Otp.Application.Abstractions;
using Otp.Application.Handlers;
using Otp.Domain.Entities;
using Otp.Domain.Events;

namespace Otp.Application.Tests;

public class OtpRequestedHandlerTests
{
    private readonly IVerificationRepository _attempts = A.Fake<IVerificationRepository>();
    private readonly IVonageVerifyClient _vonage = A.Fake<IVonageVerifyClient>();
    private readonly IEventBus _bus = A.Fake<IEventBus>();
    private readonly OtpRequestedHandler _sut;
    private readonly VerificationAttempt _attempt = new() { UserId = Guid.NewGuid() };

    public OtpRequestedHandlerTests()
    {
        _sut = new OtpRequestedHandler(_attempts, _vonage, _bus,
            new FakeTimeProvider(DateTimeOffset.UnixEpoch), NullLogger<OtpRequestedHandler>.Instance);
        A.CallTo(() => _attempts.GetAsync(_attempt.Id, A<CancellationToken>._)).Returns(_attempt);
    }

    private OtpRequested Msg() => new(_attempt.Id, _attempt.UserId, "sms", "+14155550100", "corr", DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Handle_Success_StoresRequestIdAndPublishesOtpSent()
    {
        A.CallTo(() => _vonage.StartAsync("+14155550100", "sms", A<CancellationToken>._))
            .Returns(new VerifyStartResult(true, "req-9", null));

        await _sut.HandleAsync(Msg(), default);

        _attempt.Status.Should().Be(AttemptStatus.Sent);
        _attempt.VonageRequestId.Should().Be("req-9");
        A.CallTo(() => _bus.PublishAsync(A<OtpSent>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Handle_ProviderRejects_PublishesDeliveryFailed()
    {
        A.CallTo(() => _vonage.StartAsync(A<string>._, A<string>._, A<CancellationToken>._))
            .Returns(new VerifyStartResult(false, null, "invalid_number"));

        await _sut.HandleAsync(Msg(), default);

        _attempt.Status.Should().Be(AttemptStatus.Failed);
        A.CallTo(() => _bus.PublishAsync(A<OtpDeliveryFailed>.That.Matches(e => e.Reason == "invalid_number"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Handle_DuplicateMessage_IsIgnored()
    {
        _attempt.Status = AttemptStatus.Sent;

        await _sut.HandleAsync(Msg(), default);

        A.CallTo(() => _vonage.StartAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => _bus.PublishAsync(A<OtpSent>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Handle_TransportException_PropagatesForBrokerRetry()
    {
        A.CallTo(() => _vonage.StartAsync(A<string>._, A<string>._, A<CancellationToken>._)).Throws<HttpRequestException>();

        var act = () => _sut.HandleAsync(Msg(), default);

        await act.Should().ThrowAsync<HttpRequestException>();
        _attempt.Status.Should().Be(AttemptStatus.Pending);
    }
}

public class NotificationHandlerTests
{
    [Fact]
    public async Task OtpSent_NotifiesUserOnly()
    {
        var publisher = A.Fake<INotificationPublisher>();
        var userId = Guid.NewGuid();

        await new NotificationHandler(publisher).HandleAsync(new OtpSent(Guid.NewGuid(), userId, "c", default), default);

        A.CallTo(() => publisher.NotifyAsync(userId, "OtpSent", A<object>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }
}
