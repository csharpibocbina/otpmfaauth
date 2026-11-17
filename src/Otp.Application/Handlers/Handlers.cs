using Microsoft.Extensions.Logging;
using Otp.Application.Abstractions;
using Otp.Domain.Entities;
using Otp.Domain.Events;

namespace Otp.Application.Handlers;

public interface IEventHandler<in T> where T : class, IIntegrationEvent
{
    Task HandleAsync(T message, CancellationToken ct);
}

/// <summary>Worker side: sends the OTP via Vonage. Idempotent on ChallengeId + status.</summary>
public class OtpRequestedHandler(
    IVerificationRepository attempts,
    IVonageVerifyClient vonage,
    IEventBus bus,
    TimeProvider time,
    ILogger<OtpRequestedHandler> logger) : IEventHandler<OtpRequested>
{
    public async Task HandleAsync(OtpRequested message, CancellationToken ct)
    {
        var attempt = await attempts.GetAsync(message.ChallengeId, ct);
        if (attempt is null || attempt.Status != AttemptStatus.Pending)
        {
            logger.LogInformation("Skipping OtpRequested {ChallengeId}: already handled or unknown", message.ChallengeId);
            return;
        }

        var now = time.GetUtcNow();
        // Transport exceptions propagate so the broker retries / dead-letters the message.
        var result = await vonage.StartAsync(message.Destination, message.Channel, ct);

        logger.LogInformation("Vonage start for challenge {ChallengeId}: success={Success} requestId={RequestId} error={Error}",
            message.ChallengeId, result.Success, result.RequestId, result.Error);

        if (result.Success)
        {
            attempt.Status = AttemptStatus.Sent;
            attempt.VonageRequestId = result.RequestId;
            attempt.UpdatedAt = now;
            await attempts.SaveChangesAsync(ct);
            await bus.PublishAsync(new OtpSent(attempt.Id, attempt.UserId, message.CorrelationId, now), ct);
        }
        else
        {
            attempt.Status = AttemptStatus.Failed;
            attempt.UpdatedAt = now;
            await attempts.SaveChangesAsync(ct);
            await bus.PublishAsync(new OtpDeliveryFailed(attempt.Id, attempt.UserId, result.Error ?? "unknown", message.CorrelationId, now), ct);
        }
    }
}

/// <summary>Pushes broker events to the browser through SignalR.</summary>
public class NotificationHandler(INotificationPublisher publisher) :
    IEventHandler<OtpSent>, IEventHandler<OtpDeliveryFailed>, IEventHandler<MfaVerified>, IEventHandler<MfaFailed>
{
    public Task HandleAsync(OtpSent m, CancellationToken ct) =>
        publisher.NotifyAsync(m.UserId, "OtpSent", new { m.ChallengeId }, ct);

    public Task HandleAsync(OtpDeliveryFailed m, CancellationToken ct) =>
        publisher.NotifyAsync(m.UserId, "OtpDeliveryFailed", new { m.ChallengeId, m.Reason }, ct);

    public Task HandleAsync(MfaVerified m, CancellationToken ct) =>
        publisher.NotifyAsync(m.UserId, "MfaVerified", new { m.ChallengeId }, ct);

    public Task HandleAsync(MfaFailed m, CancellationToken ct) =>
        publisher.NotifyAsync(m.UserId, "MfaFailed", new { m.ChallengeId, m.Reason }, ct);
}
