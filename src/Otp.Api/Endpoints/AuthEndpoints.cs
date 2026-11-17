using Otp.Application.Abstractions;
using Otp.Application.Common;
using Otp.Application.Services;

namespace Otp.Api.Endpoints;

public record RegisterRequest(string Email, string Password, string? Phone);
public record LoginRequest(string Email, string Password);
public record VerifyRequest(Guid ChallengeId, string Code);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        auth.MapPost("/register", async (RegisterRequest r, IAuthService svc, CancellationToken ct) =>
        {
            var res = await svc.RegisterAsync(r.Email, r.Password, r.Phone, ct);
            return res.IsSuccess ? Results.Created($"/api/users/{res.Value}", new { id = res.Value }) : res.ToProblem();
        });

        auth.MapPost("/login", async (LoginRequest r, IAuthService svc, CancellationToken ct) =>
        {
            var res = await svc.LoginAsync(r.Email, r.Password, ct);
            if (!res.IsSuccess) return res.ToProblem();
            var v = res.Value!;
            return v.Status == LoginStatus.MfaRequired
                ? Results.Json(new { mfaRequired = true, mfaToken = v.MfaToken, methods = v.Methods }, statusCode: StatusCodes.Status202Accepted)
                : Results.Ok(new { mfaRequired = false, accessToken = v.AccessToken });
        });

        var mfa = app.MapGroup("/api/mfa").RequireAuthorization("MfaStep").RequireRateLimiting("auth");

        mfa.MapPost("/challenge", async (HttpContext ctx, IMfaService svc, CancellationToken ct) =>
        {
            var res = await svc.StartChallengeAsync(ctx.UserId(), ctx.TraceIdentifier, ct);
            return res.IsSuccess
                ? Results.Accepted($"/api/mfa/challenge/{res.Value!.ChallengeId}", res.Value)
                : res.ToProblem();
        });

        mfa.MapPost("/verify", async (VerifyRequest r, HttpContext ctx, IMfaService svc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(r.Code) || r.Code.Length is < 4 or > 10 || !r.Code.All(char.IsAsciiDigit))
                return Results.Problem(statusCode: 400, title: "Code must be 4-10 digits.");
            var res = await svc.VerifyAsync(ctx.UserId(), r.ChallengeId, r.Code, ctx.TraceIdentifier, ct);
            return res.IsSuccess ? Results.Ok(new { accessToken = res.Value!.AccessToken }) : res.ToProblem();
        });

        app.MapGet("/api/me", async (HttpContext ctx, IUserRepository users, CancellationToken ct) =>
        {
            var user = await users.GetByIdAsync(ctx.UserId(), ct);
            return user is null
                ? Results.NotFound()
                : Results.Ok(new
                {
                    id = user.Id,
                    email = user.Email,
                    maskedPhone = user.PhoneE164 is null ? null : PhoneUtil.Mask(user.PhoneE164),
                    phoneVerified = user.PhoneVerifiedAt is not null,
                    mfaEnabled = user.MfaEnabled,
                    memberSince = user.CreatedAt,
                });
        }).RequireAuthorization("Access");

        return app;
    }

    private static Guid UserId(this HttpContext ctx) => Guid.Parse(ctx.User.FindFirst("sub")!.Value);

    private static IResult ToProblem<T>(this Result<T> r)
    {
        var (status, title) = r.Error switch
        {
            ErrorCode.Validation => (400, r.Message ?? "Invalid request."),
            ErrorCode.InvalidCredentials => (401, "Invalid email or password."),
            ErrorCode.AccountLocked => (423, "Account temporarily locked."),
            ErrorCode.EmailTaken => (409, "Unable to register with these details."),
            ErrorCode.NotFound => (404, r.Message ?? "Not found."),
            ErrorCode.PhoneNotVerified => (422, "No phone number on file for MFA."),
            ErrorCode.TooManyRequests => (429, r.Message ?? "Too many requests."),
            ErrorCode.ChallengeExpired => (410, "Code expired. Request a new one."),
            ErrorCode.ChallengeLocked => (423, "Too many wrong codes. Request a new one."),
            ErrorCode.InvalidCode => (400, "Incorrect code."),
            ErrorCode.ProviderError => (502, "Verification provider unavailable. Try again."),
            _ => (500, "Unexpected error."),
        };
        return Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = r.Error.ToString() });
    }
}
