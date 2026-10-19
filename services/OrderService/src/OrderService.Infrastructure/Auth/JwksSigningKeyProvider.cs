using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace OrderService.Infrastructure.Auth;

public sealed class JwtAuthOptions
{
    public const string SectionName = "JwtAuth";

    public string Issuer { get; set; } = "ecommerce-user-service";
    public string Audience { get; set; } = "ecommerce-clients";
    public string JwksUrl { get; set; } = "http://localhost:5002/.well-known/jwks.json";
    public int RefreshIntervalMinutes { get; set; } = 10;
    public int MaxStaleHours { get; set; } = 24;
}

public sealed class JwksSigningKeyProvider
{
    private readonly HttpClient _httpClient;
    private readonly JwtAuthOptions _options;
    private readonly ILogger<JwksSigningKeyProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private IReadOnlyList<SecurityKey> _cachedKeys = [];
    private DateTimeOffset _lastRefreshedUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSuccessUtc = DateTimeOffset.MinValue;

    public JwksSigningKeyProvider(
        HttpClient httpClient,
        IOptions<JwtAuthOptions> options,
        ILogger<JwksSigningKeyProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<SecurityKey> GetSigningKeys()
    {
        var now = DateTimeOffset.UtcNow;
        if (_cachedKeys.Count > 0 &&
            now - _lastRefreshedUtc < TimeSpan.FromMinutes(_options.RefreshIntervalMinutes))
        {
            return _cachedKeys;
        }

        return RefreshKeysAsync(now, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyList<SecurityKey>> RefreshKeysAsync(
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_cachedKeys.Count > 0 &&
                now - _lastRefreshedUtc < TimeSpan.FromMinutes(_options.RefreshIntervalMinutes))
            {
                return _cachedKeys;
            }

            _lastRefreshedUtc = now;

            try
            {
                var jwksJson = await _httpClient.GetStringAsync(_options.JwksUrl, ct);
                var jwks = new JsonWebKeySet(jwksJson);
                var keys = jwks.GetSigningKeys().ToList();

                if (keys.Count > 0)
                {
                    _cachedKeys = keys;
                    _lastSuccessUtc = now;
                    _logger.LogInformation("Refreshed {KeyCount} JWKS signing keys from {JwksUrl}", keys.Count, _options.JwksUrl);
                    return _cachedKeys;
                }
            }
            catch (Exception ex)
            {
                if (_cachedKeys.Count > 0 &&
                    now - _lastSuccessUtc <= TimeSpan.FromHours(_options.MaxStaleHours))
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to refresh JWKS from {JwksUrl}; continuing to use {KeyCount} cached signing keys (last refreshed at {LastSuccessUtc})",
                        _options.JwksUrl,
                        _cachedKeys.Count,
                        _lastSuccessUtc);
                    return _cachedKeys;
                }

                _logger.LogError(ex, "Failed to fetch JWKS from {JwksUrl} and no valid cached keys are available", _options.JwksUrl);
            }

            return _cachedKeys;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void SeedSigningKeysForTesting(IEnumerable<SecurityKey> keys)
    {
        _cachedKeys = keys.ToList();
        _lastRefreshedUtc = DateTimeOffset.UtcNow;
        _lastSuccessUtc = DateTimeOffset.UtcNow;
    }
}
