namespace Otp.Domain.Entities;

public enum AttemptStatus { Pending, Sent, Approved, Failed, Expired, Locked }

public class VerificationAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Channel { get; set; } = "sms";
    public string DestinationHash { get; set; } = string.Empty;
    public string? VonageRequestId { get; set; }
    public AttemptStatus Status { get; set; } = AttemptStatus.Pending;
    public int CheckCount { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
