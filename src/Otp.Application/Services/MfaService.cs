using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Otp.Application.Abstractions;
using Otp.Application.Common;
using Otp.Domain.Entities;
using Otp.Domain.Events;

namespace Otp.Application.Services;

public record ChallengeStarted(Guid ChallengeId, string MaskedDestination, DateTimeOffset ExpiresAt);
public record MfaVerifyResponse(string AccessToken);

public interface IMfaService
{
    Task<Result<ChallengeStarted>> StartChallengeAsync(Guid userId, string correlationId, CancellationToken ct);
    Task<Result<MfaVerifyResponse>> VerifyAsync(Guid userId, Guid challengeId, string code, string correlationId, CancellationToken ct);
}

public class MfaService(
    IUserRepository users,
    IVerificationRepository attempts,
    IEventBus bus,
    IVonageVerifyClient vonage,
    ITokenService tokens,
    TimeProvider time,
    IOptions<MfaOptions> options,
    ILogger<MfaService> logger) : IMfaService
{
    private readonly MfaOptions _opt = options.Value;

    public async Task<Result<ChallengeStarted>> StartChallengeAsync(Guid userId, string correlationId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null || string.IsNullOrEmpty(user.PhoneE164))
            return Result<ChallengeStarted>.Fail(ErrorCode.PhoneNotVerified);

        var now = time.GetUtcNow();

        var last = await attempts.GetLastCreatedAsync(userId, ct);
        if (last is { } l && now - l < _opt.ResendCooldown)
            return Result<ChallengeStarted>.Fail(ErrorCode.TooManyRequests, "Please wait before requesting another code.");
        if (await attempts.CountSinceAsync(userId, now - _opt.SendWindow, ct) >= _opt.MaxSendsPerWindow)
            return Result<ChallengeStarted>.Fail(ErrorCode.TooManyRequests, "Too many codes requested.");

        var attempt = new VerificationAttempt
        {
            UserId = userId,
            Channel = "sms",
            DestinationHash = PhoneUtil.Hash(user.PhoneE164),
            Status = AttemptStatus.Pending,
            ExpiresAt = now + _opt.ChallengeLifetime,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await attempts.AddAsync(attempt, ct);
        await attempts.SaveChangesAsync(ct);

        await bus.PublishAsync(new OtpRequested(attempt.Id, userId, attempt.Channel, user.PhoneE164, correlationId, now), ct);

        logger.LogInformation("OTP challenge {ChallengeId} queued for {Phone}", attempt.Id, PhoneUtil.Mask(user.PhoneE164));
        return Result<ChallengeStarted>.Ok(new(attempt.Id, PhoneUtil.Mask(user.PhoneE164), attempt.ExpiresAt));
    }

    public async Task<Result<MfaVerifyResponse>> VerifyAsync(Guid userId, Guid challengeId, string code, string correlationId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var attempt = await attempts.GetAsync(challengeId, ct);
        // Challenge must belong to the caller; do not leak existence otherwise.
        if (attempt is null || attempt.UserId != userId)
            return Result<MfaVerifyResponse>.Fail(ErrorCode.NotFound);

        if (attempt.Status == AttemptStatus.Locked)
            return Result<MfaVerifyResponse>.Fail(ErrorCode.ChallengeLocked);
        if (attempt.Status != AttemptStatus.Sent || string.IsNullOrEmpty(attempt.VonageRequestId))
            return Result<MfaVerifyResponse>.Fail(ErrorCode.NotFound, "Challenge is not awaiting a code.");
        if (attempt.ExpiresAt <= now)
        {
            attempt.Status = AttemptStatus.Expired;
            attempt.UpdatedAt = now;
            await attempts.SaveChangesAsync(ct);
            return Result<MfaVerifyResponse>.Fail(ErrorCode.ChallengeExpired);
        }

        var status = await vonage.CheckAsync(attempt.VonageRequestId, code, ct);
        attempt.CheckCount++;
        attempt.UpdatedAt = now;

        switch (status)
        {
            case VerifyCheckStatus.Approved:
                attempt.Status = AttemptStatus.Approved;
                var user = await users.GetByIdAsync(userId, ct);
                if (user is null) return Result<MfaVerifyResponse>.Fail(ErrorCode.NotFound);
                user.PhoneVerifiedAt ??= now;
                user.UpdatedAt = now;
                await users.SaveChangesAsync(ct);
                await attempts.SaveChangesAsync(ct);
                await bus.PublishAsync(new MfaVerified(attempt.Id, userId, correlationId, now), ct);
                return Result<MfaVerifyResponse>.Ok(new(tokens.CreateAccessToken(user)));

            case VerifyCheckStatus.Expired or VerifyCheckStatus.NotFound:
                attempt.Status = AttemptStatus.Expired;
                await attempts.SaveChangesAsync(ct);
                await bus.PublishAsync(new MfaFailed(attempt.Id, userId, "expired", correlationId, now), ct);
                return Result<MfaVerifyResponse>.Fail(ErrorCode.ChallengeExpired);

            case VerifyCheckStatus.Throttled:
                await attempts.SaveChangesAsync(ct);
                return Result<MfaVerifyResponse>.Fail(ErrorCode.TooManyRequests);

            case VerifyCheckStatus.Error:
                attempt.CheckCount--; // provider fault is not the user's failed attempt
                await attempts.SaveChangesAsync(ct);
                return Result<MfaVerifyResponse>.Fail(ErrorCode.ProviderError);

            default: // InvalidCode
                var locked = attempt.CheckCount >= _opt.MaxChecksPerChallenge;
                if (locked)
                {
                    attempt.Status = AttemptStatus.Locked;
                    await vonage.CancelAsync(attempt.VonageRequestId, ct);
                }
                await attempts.SaveChangesAsync(ct);
                await bus.PublishAsync(new MfaFailed(attempt.Id, userId, locked ? "locked" : "invalid_code", correlationId, now), ct);
                return Result<MfaVerifyResponse>.Fail(locked ? ErrorCode.ChallengeLocked : ErrorCode.InvalidCode);
        }
    }
}
