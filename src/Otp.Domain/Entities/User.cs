namespace Otp.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? PhoneE164 { get; set; }
    public DateTimeOffset? PhoneVerifiedAt { get; set; }
    public bool MfaEnabled { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;
}
