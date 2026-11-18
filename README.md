# OTP + MFA Auth (Vonage Verify, RabbitMQ, SignalR, Supabase)

See [Plan.md](Plan.md) for the design. Layout: `src/` (.NET 10), `tests/` (xUnit + FakeItEasy), `web/` (React + TS), `supabase/migrations/` (SQL).

## Run locally

1. **Database:** create a Supabase project, run `supabase/migrations/20261117000000_init.sql` (SQL editor or `supabase db push`).
2. **Broker:** `docker compose up -d` (RabbitMQ, UI at http://localhost:15672, guest/guest).
3. **Secrets** (never commit):
   ```
   ##Step 1 User-secrets config
   cd src/Otp.Api
   dotnet user-secrets init
   dotnet user-secrets set "ConnectionStrings:Supabase" "Host=<pooler-host>;Port=6543;Database=postgres;Username=<user>;Password=<pw>;SSL Mode=Require;Trust Server Certificate=true;No Reset On Close=true"
   ```
   ##Step 2 User-secrets config for JWT and OTP/MFA (Vonage or Twilio if preferred)
   ```
   cd src\Otp.Api
   dotnet user-secrets init
   $bytes = New-Object byte[] 48
   [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
   dotnet user-secrets set "Jwt:SigningKey" ([Convert]::ToBase64String($bytes))
   dotnet user-secrets set "Vonage:ApiKey" "84a02e4b"
   dotnet user-secrets set "Vonage:ApiSecret" "<ApiSecret value>"

   ```   
   Create the Vonage application in the dashboard with the Verify capability and download its private key.
   A trial account can only message numbers you have whitelisted.
   
5. **API:** `dotnet run --project src/Otp.Api` (default `http://localhost:5258`; if yours differs set `VITE_API_URL` in `web/.env.local`).
6. **Web:** `cd web && npm install && npm run dev` -> http://localhost:5173

## Tests

```
dotnet test          # backend
cd web && npm test   # frontend
```
