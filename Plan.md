
# Plan: OTP & MFA Authentication with Vonage

## 1. Goals

- Let users sign in with **password + a second factor** (MFA), and verify phone/email ownership with a **one-time passcode (OTP)** delivered by Vonage.
- Keep the API responsive: OTP delivery is **asynchronous** via RabbitMQ.
- Tell the browser the outcome in real time via **SignalR**.
- Persist users, OTP/MFA state and audit data in **Supabase (PostgreSQL)**.
- Cover business logic with **xUnit + FakeItEasy** unit tests.

### Non-goals (v1)
Social login, passkeys/WebAuthn, admin UI, multi-tenant support.

## 2. Tech Stack

| Layer | Choice |
|---|---|
| Frontend | React 18/19 + TypeScript, Vite, React Router, TanStack Query, `@microsoft/signalr`, Zod, React Hook Form |
| Backend | C# on .NET 10, ASP.NET Core Web API (minimal APIs or controllers), SignalR |
| Messaging | RabbitMQ (`RabbitMQ.Client` 7.x behind an `IEventBus` abstraction) |
| OTP provider | Vonage **Verify v2** API (`Vonage` NuGet SDK; authenticated with an Application ID + private key JWT) |
| TOTP (phase 2) | `Otp.NET` (Vonage Verify v2 has no TOTP/authenticator factor, so we implement RFC 6238 ourselves) |
| Database | Supabase PostgreSQL, accessed via EF Core + `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Auth tokens | JWT access token (short-lived) + rotating refresh token |
| Tests | xUnit, FakeItEasy, FluentAssertions (optional), Testcontainers (integration, later) |

> **Decision: Vonage Verify v2 instead of hand-rolled OTP.** Verify generates, delivers (SMS, WhatsApp, voice, email, with fallback workflows), expires and checks codes, so we never store or compare raw codes ourselves. We store only *verification state* (`request_id`, status, attempts). Verify v2 is workflow-based: each request defines a `brand` and an ordered `workflow` (e.g. SMS → voice fallback), a code length and a timeout. If we later need a custom code, add an `IOtpProvider` implementation without changing callers.

> **Decision: Supabase is used as managed Postgres only** (not Supabase Auth), so the .NET API owns identity, JWT issuance and MFA policy. Connect with the Supabase **pooler** connection string (transaction mode, `Pooling=true`, `No Reset On Close=true` / disable prepared statements as needed).

## 3. Architecture

```
┌────────────┐  REST (HTTPS)   ┌──────────────────┐  publish   ┌────────────┐
│ React + TS │ ──────────────► │  .NET 10 API     │ ─────────► │  RabbitMQ  │
│            │ ◄────────────── │  (Auth, MFA)     │            │ exchange   │
│            │   SignalR (WS)  │  + SignalR Hub   │ ◄───────── └─────┬──────┘
└────────────┘ ◄────────────── └───────┬──────────┘   consume        │
                                       │ EF Core                     ▼
                                       ▼                    ┌─────────────────┐
                              ┌──────────────────┐          │ OTP Worker      │
                              │ Supabase Postgres│ ◄─────── │ (HostedService) │──► Vonage Verify
                              └──────────────────┘          └─────────────────┘
```

The worker can start as a `BackgroundService` inside the API project and be split into its own process later with no code changes (it only depends on `IEventBus`, `IVonageVerifyClient`, `INotificationPublisher`).

### Solution layout

```
OTPMFAAuth/
├─ src/
│  ├─ Otp.Api/              # endpoints, SignalR hub, DI, middleware
│  ├─ Otp.Application/      # use-cases/services, DTOs, interfaces, validators
│  ├─ Otp.Domain/           # entities, enums, domain events
│  ├─ Otp.Infrastructure/   # EF Core, Vonage client, RabbitMQ, JWT, hashing
│  └─ Otp.Worker/           # (optional) standalone consumer host
├─ tests/
│  ├─ Otp.Application.Tests/
│  ├─ Otp.Infrastructure.Tests/
│  └─ Otp.Api.Tests/        # hub + endpoint tests
├─ web/                     # React + TypeScript (Vite)
└─ Plan.md
```

## 4. Core Flows

### 4.1 Sign-in with MFA
1. `POST /api/auth/login` `{ email, password }`
2. API validates password hash (Argon2id / `PasswordHasher<T>`). On success, if MFA is enabled, API returns `202 { mfaRequired: true, mfaToken, methods: ["sms","totp"] }`. `mfaToken` is a short-lived (5 min), single-purpose JWT that is **not** an access token.
3. Client opens the SignalR connection (authenticated with `mfaToken`) and calls `POST /api/mfa/challenge` `{ channel: "sms" }`.
4. API stores a `verification_attempts` row (status `Pending`), publishes `OtpRequested` to RabbitMQ and returns `202 { challengeId }` immediately.
5. Worker consumes `OtpRequested`, calls Vonage Verify v2 `POST /v2/verify` (brand, workflow, destination), stores the returned `request_id`, updates the row (`Sent`/`Failed`) and publishes `OtpSent` / `OtpDeliveryFailed`.
6. A consumer of those events pushes `OtpSent` / `OtpDeliveryFailed` to the user's SignalR group, so the UI moves to the "enter code" screen.
7. `POST /api/mfa/verify` `{ challengeId, code }` → API calls Vonage `POST /v2/verify/{request_id}` with the code (synchronous, user is waiting), updates state, publishes `MfaVerified` / `MfaFailed`, and on success returns the access + refresh tokens.
8. SignalR also notifies other open sessions/devices of the sign-in (security notification).

### 4.2 Phone number enrollment (verify ownership)
`POST /api/phone` → send OTP → `POST /api/phone/verify` → set `phone_verified_at`, enable SMS MFA.

### 4.3 TOTP (authenticator app) — phase 2
Vonage Verify does not manage authenticator-app factors, so TOTP is implemented in our API with `Otp.NET`: generate a random secret, encrypt it at rest (ASP.NET Core Data Protection or a KMS key), return an `otpauth://` URI → React renders a QR code → user confirms with a first code (±1 time-step window, replay-protected by storing the last used time-step) → factor becomes `Verified`. Recovery codes (10, single use, stored hashed) are issued at enrollment.

### 4.4 Resend / rate limiting
- Resend allowed after 30 s, max 5 sends per 10 min per user and per phone (enforced in API **and** by Vonage Verify's own limits; a new request for the same number cancels or conflicts with the pending one, which the adapter maps to a domain result — cancel the old `request_id` via `DELETE /v2/verify/{request_id}` before resending).
- Max 5 failed checks per challenge → challenge `Locked`; account-level lockout with exponential backoff.
- ASP.NET Core `RateLimiter` middleware on `/api/auth/*` and `/api/mfa/*` (per IP + per user).

## 5. Messaging (RabbitMQ)

- Exchange: `otp.events` (topic, durable). Queues: `otp.requested` (worker), `otp.notifications` (SignalR publisher), each with a DLQ `*.dlq`.
- Routing keys: `otp.requested`, `otp.sent`, `otp.delivery_failed`, `mfa.verified`, `mfa.failed`.
- Message contract (versioned records in `Otp.Domain/Events`): `OtpRequested(Guid ChallengeId, Guid UserId, string Channel, string Destination /* E.164 */, string CorrelationId, DateTimeOffset At)`.
- Reliability: publisher confirms, persistent messages, manual ack, **idempotent consumers** (dedupe on `ChallengeId` + status), retry with backoff (3 tries) then DLQ.
- Never put OTP codes in messages or logs. Mask phone numbers (`+1•••••1234`).
- Optional hardening: **transactional outbox** table in Postgres so "save row + publish event" is atomic.

## 6. SignalR

- Hub: `/hubs/notifications`, JWT via `accessTokenFactory` (query-string token handled in `OnMessageReceived` for WebSockets).
- Groups: `user:{userId}`; added in `OnConnectedAsync`.
- Server → client events: `OtpSent`, `OtpDeliveryFailed`, `MfaVerified`, `MfaFailed`, `NewSignIn`.
- Strongly typed hub (`Hub<INotificationClient>`); publishing goes through `INotificationPublisher` so it is mockable.
- Scale-out later: Redis backplane (or sticky sessions).

## 7. Database (Supabase / PostgreSQL)

Managed with EF Core migrations (`snake_case` naming via `EFCore.NamingConventions`). **Enable RLS** on all tables and deny `anon`/`authenticated` roles, since only the API (service connection) touches the data.

```sql
users(
  id uuid pk, email citext unique not null, password_hash text not null,
  phone_e164 text, phone_verified_at timestamptz,
  mfa_enabled bool not null default false, failed_login_count int not null default 0,
  locked_until timestamptz, created_at timestamptz, updated_at timestamptz)

mfa_factors(
  id uuid pk, user_id uuid fk, type text /* sms | totp */, totp_secret_encrypted bytea, totp_last_step bigint,
  status text /* pending | verified | revoked */, created_at, verified_at)

verification_attempts(
  id uuid pk, user_id uuid fk, channel text, destination_hash text,
  vonage_request_id text, status text /* pending|sent|approved|failed|expired|locked */,
  check_count int not null default 0, expires_at timestamptz, created_at, updated_at)

recovery_codes(id uuid pk, user_id fk, code_hash text, used_at timestamptz)
refresh_tokens(id uuid pk, user_id fk, token_hash text, expires_at, revoked_at, replaced_by uuid)
audit_log(id bigserial pk, user_id, event text, ip inet, user_agent text, correlation_id, at timestamptz)
outbox_messages(id uuid pk, type text, payload jsonb, created_at, processed_at)  -- optional
```

Indexes: `verification_attempts(user_id, created_at desc)`, `refresh_tokens(token_hash)`, `audit_log(user_id, at desc)`.

## 8. Backend Components (testable seams)

| Interface | Responsibility | Real impl |
|---|---|---|
| `IAuthService` | login, mfaToken issue, token refresh | `AuthService` |
| `IMfaService` | start challenge, verify code, lockout rules | `MfaService` |
| `IOtpProvider` / `IVonageVerifyClient` | send, check & cancel codes | `VonageVerifyClient` |
| `ITotpService` | TOTP secret generation, validation, replay protection | `TotpService` (Otp.NET) |
| `IEventBus` | publish/subscribe | `RabbitMqEventBus` |
| `INotificationPublisher` | push to SignalR | `SignalRNotificationPublisher` |
| `IUserRepository`, `IVerificationRepository` | persistence | EF Core repos |
| `ITokenService` | JWT / refresh tokens | `JwtTokenService` |
| `IClock` (`TimeProvider`) | time | `TimeProvider.System` |

Config (user-secrets / env, never committed): `Vonage:ApplicationId`, `Vonage:PrivateKey` (PEM contents or path; secret), `Vonage:Brand` (name shown in the message), `Vonage:CodeLength`, `Vonage:TimeoutSeconds`, optionally `Vonage:ApiKey`/`ApiSecret` (needed only for signed webhooks/legacy APIs), `RabbitMq:*`, `ConnectionStrings:Supabase`, `Jwt:*`.

## 9. Frontend (React + TypeScript)

```
web/src/
├─ api/            # typed fetch client, Zod schemas, error mapping
├─ auth/           # AuthProvider, token storage, route guards
├─ realtime/       # SignalR connection hook (useNotifications)
├─ pages/          # Login, MfaChallenge, EnrollPhone, EnrollTotp, Security
├─ components/     # OtpInput (6-digit, paste + autofill), CountdownResend, QrCode
└─ tests/          # Vitest + Testing Library
```

- State machine for login: `credentials → mfaRequired → awaitingCode → verified`.
- `OtpInput` uses `autocomplete="one-time-code"` and `inputMode="numeric"`.
- Access token in memory; refresh token in an `HttpOnly; Secure; SameSite=Strict` cookie. No tokens in `localStorage`.
- SignalR reconnect with backoff; fall back to polling `GET /api/mfa/challenge/{id}` if the socket is down.
- CORS locked to the web origin; `withCredentials` for the refresh cookie.

## 10. Security Checklist

- Generic error messages (no account/phone enumeration); constant-time comparisons.
- Argon2id/PBKDF2 password hashing, refresh-token rotation with reuse detection.
- OTP expiry 5 min (Vonage `timeout` default 300 s, configurable 60–900 s), max 5 checks enforced by us in addition to Vonage's own limits, lockout + audit.
- Vonage webhook (if Verify status callbacks are used) validated by verifying the signed JWT in the `Authorization: Bearer` header against our signature secret.
- Secrets via user-secrets locally, Key Vault / env vars in deploy; `AuthToken` rotated.
- Vonage **Fraud Defender / Traffic Rules** (country allow-list, spend limits) and account balance alerts enabled to limit SMS pumping; per-IP and per-phone limits on our side.
- HTTPS + HSTS, security headers, structured logging with PII redaction, correlation IDs end to end.

## 11. Unit Testing (xUnit + FakeItEasy)

Conventions: one test class per unit, `MethodUnderTest_Scenario_ExpectedResult` naming, Arrange/Act/Assert, `A.Fake<T>()` for every collaborator, `TimeProvider` faked/`FakeTimeProvider` for time, no real network/DB/broker in unit tests.

Packages: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FakeItEasy`, `FluentAssertions` (optional), `Microsoft.Extensions.TimeProvider.Testing`, `coverlet.collector`.

### Example

```csharp
public class MfaServiceTests
{
    private readonly IVerificationRepository _repo = A.Fake<IVerificationRepository>();
    private readonly IEventBus _bus = A.Fake<IEventBus>();
    private readonly IVonageVerifyClient _vonage = A.Fake<IVonageVerifyClient>();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    private readonly MfaService _sut;

    public MfaServiceTests() =>
        _sut = new MfaService(_repo, _bus, _vonage, _time, A.Fake<ILogger<MfaService>>());

    [Fact]
    public async Task StartChallenge_ValidUser_SavesAttemptAndPublishesOtpRequested()
    {
        var user = new User { Id = Guid.NewGuid(), PhoneE164 = "+14155550100", PhoneVerifiedAt = _time.GetUtcNow() };

        var result = await _sut.StartChallengeAsync(user, "sms", default);

        result.IsSuccess.Should().BeTrue();
        A.CallTo(() => _repo.AddAsync(A<VerificationAttempt>.That.Matches(v => v.UserId == user.Id), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _bus.PublishAsync(A<OtpRequested>.That.Matches(e => e.ChallengeId == result.Value.ChallengeId),
                A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _vonage.SendAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Verify_WrongCode_IncrementsCountAndLocksAfterFiveFailures()
    {
        var attempt = new VerificationAttempt { CheckCount = 4, Status = AttemptStatus.Sent };
        A.CallTo(() => _repo.GetAsync(attempt.Id, A<CancellationToken>._)).Returns(attempt);
        A.CallTo(() => _vonage.CheckAsync(A<string>._, "000000", A<CancellationToken>._)).Returns(false);

        var result = await _sut.VerifyAsync(attempt.Id, "000000", default);

        result.IsSuccess.Should().BeFalse();
        attempt.Status.Should().Be(AttemptStatus.Locked);
        A.CallTo(() => _bus.PublishAsync(A<MfaFailed>._, A<CancellationToken>._)).MustHaveHappened();
    }
}
```

### Test matrix

| Unit | Scenarios |
|---|---|
| `AuthService` | bad password increments failure count; lockout at threshold; MFA-enabled user gets `mfaToken` not access token; MFA-disabled user gets tokens; refresh-token reuse revokes the family |
| `MfaService` | start challenge (happy path, unverified phone, resend too soon, over rate limit); verify (approved, wrong code, expired, locked, already used); recovery code single-use |
| `OtpRequestedConsumer` | calls Vonage once; publishes `OtpSent`; Vonage exception → `OtpDeliveryFailed`; duplicate message is ignored (idempotency); transient failure rethrown for retry |
| `VonageVerifyClient` | maps Verify v2 responses (200 approved, 400 invalid code, 404 not found/expired, 409 conflict/concurrent request, 410 already verified, 429 throttled) to domain results (wrap the SDK behind a thin adapter so it can be faked) |
| `TotpService` | valid code, ±1 step drift accepted, replayed step rejected, wrong secret rejected (use a fake `TimeProvider`) |
| `SignalRNotificationPublisher` | sends to group `user:{id}` with the correct method/payload (fake `IHubContext<NotificationHub, INotificationClient>`) |
| `JwtTokenService` | claims, expiry, `mfaToken` has purpose claim and cannot be used as access token |
| Validators | E.164 phone, 6-digit code, channel whitelist |
| Endpoints (WebApplicationFactory, fakes injected) | 401/403/429 mapping, ProblemDetails shape |

Frontend: Vitest + React Testing Library for `OtpInput`, login state machine, SignalR hook (mock connection).

Targets: ≥ 85 % line coverage on `Otp.Application`, 100 % of branches in lockout/expiry logic. Later: Testcontainers (Postgres + RabbitMQ) integration tests and a Vonage smoke test against a dedicated dev application (own verified number, low spend limit) run manually or nightly, never on every PR.

## 12. Delivery Phases

| Phase | Scope | Exit criteria |
|---|---|---|
| 0 – Setup | Solution + projects, Supabase project, Vonage account + Application (Verify capability, private key), RabbitMQ via docker-compose, CI build/test | `dotnet test` and `npm test` green in CI |
| 1 – Core auth | Users table, register/login, JWT, refresh rotation, unit tests | Login works without MFA |
| 2 – SMS OTP | Challenge/verify endpoints, RabbitMQ publish + worker, Vonage Verify, DLQ | OTP received on a real phone, tested end to end |
| 3 – Real-time | SignalR hub, publisher, React hook, UI states | UI advances on `OtpSent` without polling |
| 4 – Hardening | Rate limits, lockouts, audit log, idempotency, outbox, observability (OpenTelemetry) | Security checklist complete |
| 5 – TOTP & recovery | Authenticator enrollment, QR code, recovery codes | Second factor choice in UI |
| 6 – Release | Load test, pen-test checklist, runbook, deploy | Go-live review |

## 13. Local Development

- `docker-compose.yml`: RabbitMQ (`rabbitmq:4-management`, ports 5672/15672); optional local Postgres mirroring Supabase.
- `dotnet user-secrets` for Vonage/JWT/DB; `.env.local` for the Vite `VITE_API_URL`.
- Vonage trial/demo account: only whitelisted (own) numbers can receive messages until you add credit; use a separate Vonage Application for dev.
- Run: `dotnet run --project src/Otp.Api` and `npm run dev` in `web/`.

## 14. Open Questions

1. Channels in v1: SMS only, or also email / WhatsApp / voice?
2. Is MFA mandatory for all users or opt-in?
3. Hosting target (Azure, AWS, Fly.io, etc.) — affects the RabbitMQ choice (self-host vs. CloudAMQP) and the SignalR backplane.
4. Compliance needs (GDPR retention for `audit_log`, SMS consent/A2P 10DLC registration for US numbers)?

## 15. Implementation Status (updated 2026-11-17)

| Item | Status |
|---|---|
| Solution scaffold (Domain / Application / Infrastructure / Api, xUnit test projects) | Done |
| Register + login, Argon2id hashing, lockout, `mfaToken` vs access token (`purpose` claim) | Done, unit-tested |
| SMS OTP: challenge → RabbitMQ `otp.requested` → worker → Vonage Verify v2 → `otp.sent` → SignalR | Done, unit-tested (broker, DB and Vonage not exercised end to end) |
| Vonage adapter | Implemented with `HttpClient` against the Verify v2 REST API (no `Vonage` NuGet package); RS256 application JWT |
| RabbitMQ | Publisher confirms, durable topic exchange, per-subscription queues, 3 in-process retries with backoff, then DLQ |
| SignalR hub + typed client, JWT via query string | Done |
| Supabase SQL migration with RLS | Done (`supabase/migrations`) |
| React: login, register, MFA challenge (OtpInput, resend cooldown, SignalR with fallback) | Done |
| Not yet done | Refresh tokens, phone-enrollment endpoints, TOTP + recovery codes, outbox table, audit log, OpenTelemetry, Testcontainers integration tests, CI pipeline |
| Known gap | A resend while the previous Vonage request is still active may return 409 `conflict_pending_request`; cancel the previous `request_id` before starting a new challenge |
