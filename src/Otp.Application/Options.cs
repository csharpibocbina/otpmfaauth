namespace Otp.Application;

public class MfaOptions
{
    public int MaxChecksPerChallenge { get; set; } = 5;
    public int MaxSendsPerWindow { get; set; } = 5;
    public TimeSpan SendWindow { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(5);
}

public class LockoutOptions
{
    public int MaxFailedLogins { get; set; } = 5;
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(15);
}
