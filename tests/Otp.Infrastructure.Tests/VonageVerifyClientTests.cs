using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Otp.Application.Abstractions;
using Otp.Infrastructure.Vonage;

namespace Otp.Infrastructure.Tests;

public class VonageVerifyClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body = "{}") : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static (VonageVerifyClient Sut, StubHandler Handler) Create(HttpStatusCode status, string body = "{}")
    {
        using var rsa = RSA.Create(2048);
        var handler = new StubHandler(status, body);
        var opts = Options.Create(new VonageOptions
        {
            ApplicationId = "app-123", PrivateKey = rsa.ExportPkcs8PrivateKeyPem(), BaseUrl = "https://vonage.test/v2/verify",
        });
        return (new VonageVerifyClient(new HttpClient(handler), opts, new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<VonageVerifyClient>.Instance), handler);
    }

    [Fact]
    public async Task Start_Accepted_ReturnsRequestId_AndSendsNumberWithoutPlus()
    {
        var (sut, h) = Create(HttpStatusCode.Accepted, """{"request_id":"abc-1"}""");

        var r = await sut.StartAsync("+14155550100", "sms", default);

        r.Should().Be(new VerifyStartResult(true, "abc-1", null));
        h.LastBody.Should().Contain("14155550100").And.NotContain("+1415");
        h.Last!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        h.Last.Headers.Authorization.Parameter.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "conflict_pending_request")]
    [InlineData(HttpStatusCode.TooManyRequests, "throttled")]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request")]
    [InlineData(HttpStatusCode.Unauthorized, "provider_auth")]
    public async Task Start_ClientErrors_AreMappedToFailureReasons(HttpStatusCode code, string reason)
    {
        var (sut, _) = Create(code);
        var r = await sut.StartAsync("+14155550100", "sms", default);
        r.Success.Should().BeFalse();
        r.Error.Should().Be(reason);
    }

    [Fact]
    public async Task Start_ServerError_ThrowsSoBrokerCanRetry()
    {
        var (sut, _) = Create(HttpStatusCode.BadGateway);
        var act = () => sut.StartAsync("+14155550100", "sms", default);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, VerifyCheckStatus.Approved)]
    [InlineData(HttpStatusCode.BadRequest, VerifyCheckStatus.InvalidCode)]
    [InlineData(HttpStatusCode.NotFound, VerifyCheckStatus.NotFound)]
    [InlineData(HttpStatusCode.Conflict, VerifyCheckStatus.Expired)]
    [InlineData(HttpStatusCode.Gone, VerifyCheckStatus.Expired)]
    [InlineData(HttpStatusCode.TooManyRequests, VerifyCheckStatus.Throttled)]
    [InlineData(HttpStatusCode.InternalServerError, VerifyCheckStatus.Error)]
    public async Task Check_MapsHttpStatusToDomainStatus(HttpStatusCode code, VerifyCheckStatus expected)
    {
        var (sut, h) = Create(code);
        var r = await sut.CheckAsync("abc-1", "123456", default);
        r.Should().Be(expected);
        h.Last!.RequestUri!.AbsolutePath.Should().EndWith("/abc-1");
        h.LastBody.Should().Contain("123456");
    }

    [Fact]
    public async Task Cancel_IssuesDelete_AndNeverThrowsOnFailure()
    {
        var (sut, h) = Create(HttpStatusCode.InternalServerError);
        await sut.CancelAsync("abc-1", default);
        h.Last!.Method.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public async Task Auth_WithApiKeyAndSecret_UsesBasicHeader()
    {
        var handler = new StubHandler(HttpStatusCode.Accepted, """{"request_id":"r1"}""");
        var opts = Options.Create(new VonageOptions { ApiKey = "key", ApiSecret = "secret", BaseUrl = "https://vonage.test/v2/verify" });
        var sut = new VonageVerifyClient(new HttpClient(handler), opts, new FakeTimeProvider(DateTimeOffset.UtcNow), NullLogger<VonageVerifyClient>.Instance);

        await sut.StartAsync("+14155550100", "sms", default);

        handler.Last!.Headers.Authorization!.Scheme.Should().Be("Basic");
        Encoding.UTF8.GetString(Convert.FromBase64String(handler.Last.Headers.Authorization.Parameter!)).Should().Be("key:secret");
    }

    [Fact]
    public async Task Auth_WithoutApiKey_FallsBackToJwtBearer()
    {
        var (sut, h) = Create(HttpStatusCode.Accepted, """{"request_id":"r1"}""");
        await sut.StartAsync("+14155550100", "sms", default);
        h.Last!.Headers.Authorization!.Scheme.Should().Be("Bearer");
    }
}
