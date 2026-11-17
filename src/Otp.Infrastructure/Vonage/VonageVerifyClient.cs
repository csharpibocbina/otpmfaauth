using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Otp.Application.Abstractions;

namespace Otp.Infrastructure.Vonage;

public class VonageOptions
{
    /// <summary>API key + secret (HTTP Basic). Preferred when both are set.</summary>
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    /// <summary>Application JWT auth, used only when no API key/secret is configured.</summary>
    public string ApplicationId { get; set; } = string.Empty;
    /// <summary>PEM contents of the application's private key (secret).</summary>
    public string PrivateKey { get; set; } = string.Empty;
    public string Brand { get; set; } = "OtpMfaAuth";
    public int CodeLength { get; set; } = 6;
    /// <summary>Per-channel timeout in seconds (Verify v2 accepts 60-900).</summary>
    public int ChannelTimeoutSeconds { get; set; } = 300;
    public string BaseUrl { get; set; } = "https://api.nexmo.com/v2/verify";
}

/// <summary>Thin adapter over Vonage Verify v2 REST. Everything provider-specific lives here.</summary>
public class VonageVerifyClient(
    HttpClient http,
    IOptions<VonageOptions> options,
    TimeProvider time,
    ILogger<VonageVerifyClient> logger) : IVonageVerifyClient
{
    private readonly VonageOptions _o = options.Value;
    private bool UseBasicAuth => !string.IsNullOrEmpty(_o.ApiKey) && !string.IsNullOrEmpty(_o.ApiSecret);

    public async Task<VerifyStartResult> StartAsync(string destinationE164, string channel, CancellationToken ct)
    {
        // Vonage expects the number without the leading '+'.
        var to = destinationE164.TrimStart('+');
        var body = new
        {
            brand = _o.Brand,
            code_length = _o.CodeLength,
            channel_timeout = _o.ChannelTimeoutSeconds,
            workflow = new[] { new { channel, to } },
        };
        using var req = Request(HttpMethod.Post, _o.BaseUrl, JsonContent.Create(body));
        using var res = await http.SendAsync(req, ct);

        if (res.StatusCode == HttpStatusCode.Accepted || res.StatusCode == HttpStatusCode.Created || res.StatusCode == HttpStatusCode.OK)
        {
            var json = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            var id = json.TryGetProperty("request_id", out var rid) ? rid.GetString() : null;
            return id is null ? new(false, null, "no_request_id") : new(true, id, null);
        }

        // 5xx / network problems are transient: throw so the broker retries the message.
        if ((int)res.StatusCode >= 500) throw new HttpRequestException($"Vonage Verify returned {(int)res.StatusCode}");

        var detail = Regex.Replace(await res.Content.ReadAsStringAsync(ct), @"\d{8,}", "***");
        logger.LogWarning("Vonage Verify start rejected with {Status}: {Detail}", (int)res.StatusCode, detail);
        return new(false, null, res.StatusCode switch
        {
            HttpStatusCode.Conflict => "conflict_pending_request",
            HttpStatusCode.TooManyRequests => "throttled",
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "invalid_request",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "provider_auth",
            _ => "rejected",
        });
    }

    public async Task<VerifyCheckStatus> CheckAsync(string requestId, string code, CancellationToken ct)
    {
        try
        {
            using var req = Request(HttpMethod.Post, $"{_o.BaseUrl}/{Uri.EscapeDataString(requestId)}", JsonContent.Create(new { code }));
            using var res = await http.SendAsync(req, ct);
            return res.StatusCode switch
            {
                HttpStatusCode.OK => VerifyCheckStatus.Approved,
                HttpStatusCode.BadRequest => VerifyCheckStatus.InvalidCode,
                HttpStatusCode.NotFound => VerifyCheckStatus.NotFound,
                HttpStatusCode.Conflict or HttpStatusCode.Gone => VerifyCheckStatus.Expired,
                HttpStatusCode.TooManyRequests => VerifyCheckStatus.Throttled,
                _ => VerifyCheckStatus.Error,
            };
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Vonage Verify check failed");
            return VerifyCheckStatus.Error;
        }
    }

    public async Task CancelAsync(string requestId, CancellationToken ct)
    {
        try
        {
            using var req = Request(HttpMethod.Delete, $"{_o.BaseUrl}/{Uri.EscapeDataString(requestId)}", null);
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode && res.StatusCode != HttpStatusCode.NotFound)
                logger.LogWarning("Vonage Verify cancel returned {Status}", (int)res.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Vonage Verify cancel failed (non-fatal)");
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string url, HttpContent? content)
    {
        var req = new HttpRequestMessage(method, url) { Content = content };
        req.Headers.Authorization = UseBasicAuth
            ? new("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_o.ApiKey}:{_o.ApiSecret}")))
            : new("Bearer", CreateJwt());
        return req;
    }

    /// <summary>Vonage application JWT: RS256 with application_id, iat, jti, exp.</summary>
    internal string CreateJwt()
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(_o.PrivateKey);
        var now = time.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            IssuedAt = now,
            Expires = now.AddMinutes(5),
            Claims = new Dictionary<string, object>
            {
                ["application_id"] = _o.ApplicationId,
                ["jti"] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } }, SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
