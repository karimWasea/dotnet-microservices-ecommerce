using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace UserService.Infrastructure.Security;

public sealed record JwkItem(
    string kty,
    string use,
    string alg,
    string kid,
    string n,
    string e);

public sealed record JwksDocument(IReadOnlyList<JwkItem> keys);

public sealed class RsaKeyMaterial : IDisposable
{
    private readonly RSA _signingRsa;
    private readonly RSA _validationRsa;
    private readonly JwksDocument _jwksDocument;

    public string KeyId { get; }
    public RsaSecurityKey SigningKey { get; }
    public SigningCredentials SigningCredentials { get; }
    public RsaSecurityKey ValidationKey { get; }

    public RsaKeyMaterial(
        IOptions<JwtOptions> jwtOptions,
        ILogger<RsaKeyMaterial>? logger = null)
    {
        var options = jwtOptions.Value;
        KeyId = string.IsNullOrWhiteSpace(options.KeyId)
            ? "userservice-dev-key-1"
            : options.KeyId.Trim();

        _signingRsa = RSA.Create(2048);
        LoadOrGeneratePrivateKey(_signingRsa, options, logger);

        SigningKey = new RsaSecurityKey(_signingRsa)
        {
            KeyId = KeyId
        };

        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256);

        var publicParameters = _signingRsa.ExportParameters(includePrivateParameters: false);
        _validationRsa = RSA.Create();
        _validationRsa.ImportParameters(publicParameters);

        ValidationKey = new RsaSecurityKey(_validationRsa)
        {
            KeyId = KeyId
        };

        var modulus = Base64UrlEncoder.Encode(publicParameters.Modulus!);
        var exponent = Base64UrlEncoder.Encode(publicParameters.Exponent!);

        _jwksDocument = new JwksDocument(
        [
            new JwkItem(
                kty: "RSA",
                use: "sig",
                alg: SecurityAlgorithms.RsaSha256,
                kid: KeyId,
                n: modulus,
                e: exponent)
        ]);
    }

    public JwksDocument GetJwksDocument() => _jwksDocument;

    private static void LoadOrGeneratePrivateKey(
        RSA rsa,
        JwtOptions options,
        ILogger<RsaKeyMaterial>? logger)
    {
        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPem))
        {
            var normalizedPem = options.PrivateKeyPem.Replace("\\n", "\n", StringComparison.Ordinal);
            rsa.ImportFromPem(normalizedPem);
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPath))
        {
            var fullPath = Path.GetFullPath(options.PrivateKeyPath);
            if (File.Exists(fullPath))
            {
                var pem = File.ReadAllText(fullPath);
                rsa.ImportFromPem(pem);
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var exportedPem = rsa.ExportPkcs8PrivateKeyPem();
                File.WriteAllText(fullPath, exportedPem);
                logger?.LogInformation("Generated new Development RSA signing key at {PrivateKeyPath} (kid={KeyId}).", fullPath, options.KeyId);
                return;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Could not persist Development RSA key to {PrivateKeyPath}; using in-memory RSA key.", fullPath);
            }
        }
    }

    public void Dispose()
    {
        _signingRsa.Dispose();
        _validationRsa.Dispose();
    }
}
