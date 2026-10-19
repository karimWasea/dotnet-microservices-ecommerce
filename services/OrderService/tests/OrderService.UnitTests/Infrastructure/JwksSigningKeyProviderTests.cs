using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OrderService.Infrastructure.Auth;

namespace OrderService.UnitTests.Infrastructure;

public sealed class JwksSigningKeyProviderTests
{
    [Fact]
    public async Task GetSigningKeys_WhenCachedAndFresh_ReturnsCachedKeysWithoutHttpCall()
    {
        var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key-1" };

        var handler = new MockHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"));
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new JwtAuthOptions { RefreshIntervalMinutes = 10 });
        var provider = new JwksSigningKeyProvider(httpClient, options, NullLogger<JwksSigningKeyProvider>.Instance);

        provider.SeedSigningKeysForTesting([key]);

        var keys = provider.GetSigningKeys();

        Assert.Single(keys);
        Assert.Equal("test-key-1", keys[0].KeyId);
    }

    [Fact]
    public async Task RefreshKeysAsync_WhenHttpFailsAndCachedKeyWithin24Hours_ReturnsCachedKey()
    {
        var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "fallback-key" };

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new JwtAuthOptions { RefreshIntervalMinutes = 10, MaxStaleHours = 24 });
        var provider = new JwksSigningKeyProvider(httpClient, options, NullLogger<JwksSigningKeyProvider>.Instance);

        provider.SeedSigningKeysForTesting([key]);

        // Refresh at now + 15 min (stale, but well within 24h)
        var keys = await provider.RefreshKeysAsync(DateTimeOffset.UtcNow.AddMinutes(15));

        Assert.Single(keys);
        Assert.Equal("fallback-key", keys[0].KeyId);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
