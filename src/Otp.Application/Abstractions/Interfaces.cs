using Otp.Domain.Entities;
using Otp.Domain.Events;

namespace Otp.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IVerificationRepository
{
    Task AddAsync(VerificationAttempt attempt, CancellationToken ct);
    Task<VerificationAttempt?> GetAsync(Guid id, CancellationToken ct);
    Task<int> CountSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct);
    Task<DateTimeOffset?> GetLastCreatedAsync(Guid userId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IEventBus
{
    Task PublishAsync<T>(T message, CancellationToken ct) where T : class, IIntegrationEvent;
}

public enum VerifyCheckStatus { Approved, InvalidCode, Expired, NotFound, Throttled, Error }

public record VerifyStartResult(bool Success, string? RequestId, string? Error);

public interface IVonageVerifyClient
{
    Task<VerifyStartResult> StartAsync(string destinationE164, string channel, CancellationToken ct);
    Task<VerifyCheckStatus> CheckAsync(string requestId, string code, CancellationToken ct);
    Task CancelAsync(string requestId, CancellationToken ct);
}

public interface INotificationPublisher
{
    Task NotifyAsync(Guid userId, string eventName, object payload, CancellationToken ct);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface ITokenService
{
    string CreateAccessToken(User user);
    /// <summary>Short-lived token proving the password step passed. Cannot access normal endpoints.</summary>
    string CreateMfaToken(User user);
}
