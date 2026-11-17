using System.Net.Mail;
using Microsoft.Extensions.Options;
using Otp.Application.Abstractions;
using Otp.Application.Common;
using Otp.Domain.Entities;

namespace Otp.Application.Services;

public enum LoginStatus { Success, MfaRequired }

public record LoginResponse(LoginStatus Status, string? AccessToken, string? MfaToken, string[] Methods);

public interface IAuthService
{
    Task<Result<Guid>> RegisterAsync(string email, string password, string? phoneE164, CancellationToken ct);
    Task<Result<LoginResponse>> LoginAsync(string email, string password, CancellationToken ct);
}

public class AuthService(
    IUserRepository users,
    IPasswordHasher hasher,
    ITokenService tokens,
    TimeProvider time,
    IOptions<LockoutOptions> lockout) : IAuthService
{
    private readonly LockoutOptions _lockout = lockout.Value;

    public async Task<Result<Guid>> RegisterAsync(string email, string password, string? phoneE164, CancellationToken ct)
    {
        email = email.Trim().ToLowerInvariant();
        if (!MailAddress.TryCreate(email, out _) || password.Length < 12)
            return Result<Guid>.Fail(ErrorCode.Validation, "Valid email and a password of at least 12 characters are required.");
        if (phoneE164 is not null && !PhoneUtil.IsE164(phoneE164))
            return Result<Guid>.Fail(ErrorCode.Validation, "Phone must be in E.164 format.");
        if (await users.GetByEmailAsync(email, ct) is not null)
            return Result<Guid>.Fail(ErrorCode.EmailTaken);

        var now = time.GetUtcNow();
        var user = new User
        {
            Email = email,
            PasswordHash = hasher.Hash(password),
            PhoneE164 = phoneE164,
            // Phone ownership is proven by the first successful OTP (phase 2 enrollment flow).
            MfaEnabled = phoneE164 is not null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await users.AddAsync(user, ct);
        await users.SaveChangesAsync(ct);
        return Result<Guid>.Ok(user.Id);
    }

    public async Task<Result<LoginResponse>> LoginAsync(string email, string password, CancellationToken ct)
    {
        var user = await users.GetByEmailAsync(email.Trim().ToLowerInvariant(), ct);
        var now = time.GetUtcNow();

        // Same response for unknown user and wrong password: no account enumeration.
        if (user is null)
            return Result<LoginResponse>.Fail(ErrorCode.InvalidCredentials);

        if (user.IsLocked(now))
            return Result<LoginResponse>.Fail(ErrorCode.AccountLocked);

        if (!hasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= _lockout.MaxFailedLogins)
            {
                user.LockedUntil = now + _lockout.LockDuration;
                user.FailedLoginCount = 0;
            }
            user.UpdatedAt = now;
            await users.SaveChangesAsync(ct);
            return Result<LoginResponse>.Fail(ErrorCode.InvalidCredentials);
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.UpdatedAt = now;
        await users.SaveChangesAsync(ct);

        return user.MfaEnabled
            ? Result<LoginResponse>.Ok(new(LoginStatus.MfaRequired, null, tokens.CreateMfaToken(user), ["sms"]))
            : Result<LoginResponse>.Ok(new(LoginStatus.Success, tokens.CreateAccessToken(user), null, []));
    }
}
