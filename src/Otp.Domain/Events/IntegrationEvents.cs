namespace Otp.Domain.Events;

/// <summary>Marker for messages that travel over the broker. RoutingKey is stable and versionable.</summary>
public interface IIntegrationEvent
{
    static abstract string RoutingKey { get; }
    Guid UserId { get; }
    string CorrelationId { get; }
}

public record OtpRequested(Guid ChallengeId, Guid UserId, string Channel, string Destination, string CorrelationId, DateTimeOffset At) : IIntegrationEvent
{
    public static string RoutingKey => "otp.requested";
}

public record OtpSent(Guid ChallengeId, Guid UserId, string CorrelationId, DateTimeOffset At) : IIntegrationEvent
{
    public static string RoutingKey => "otp.sent";
}

public record OtpDeliveryFailed(Guid ChallengeId, Guid UserId, string Reason, string CorrelationId, DateTimeOffset At) : IIntegrationEvent
{
    public static string RoutingKey => "otp.delivery_failed";
}

public record MfaVerified(Guid ChallengeId, Guid UserId, string CorrelationId, DateTimeOffset At) : IIntegrationEvent
{
    public static string RoutingKey => "mfa.verified";
}

public record MfaFailed(Guid ChallengeId, Guid UserId, string Reason, string CorrelationId, DateTimeOffset At) : IIntegrationEvent
{
    public static string RoutingKey => "mfa.failed";
}
