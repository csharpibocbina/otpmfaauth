namespace Otp.Application.Common;

public enum ErrorCode
{
    None, Validation, InvalidCredentials, AccountLocked, EmailTaken, NotFound,
    PhoneNotVerified, TooManyRequests, ChallengeExpired, ChallengeLocked, InvalidCode, ProviderError
}

public record Result<T>(bool IsSuccess, T? Value, ErrorCode Error, string? Message = null)
{
    public static Result<T> Ok(T value) => new(true, value, ErrorCode.None);
    public static Result<T> Fail(ErrorCode error, string? message = null) => new(false, default, error, message);
}
