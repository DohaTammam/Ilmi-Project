using System.Security.Cryptography;
using System.Text;
using Ilmi.Modules.UsersIdentity.Application;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;

namespace Ilmi.Modules.UsersIdentity.Infrastructure.ExternalServices;

/// <summary>Secrets come from configuration (user-secrets or environment variables). Nothing is hardcoded.</summary>
public sealed class IdentityCryptoOptions
{
    /// <summary>Base64, at least 32 bytes. Key of the HMAC that protects OTP codes.</summary>
    public string? OtpHmacKey { get; set; }
    /// <summary>Base64, exactly 32 bytes. Key of the AES-GCM encryption of pending payloads.</summary>
    public string? PayloadEncryptionKey { get; set; }

    internal byte[] RequireOtpKey()
    {
        var key = Decode(OtpHmacKey, "OtpHmacKey");
        return key.Length >= 32 ? key : throw new InvalidOperationException("UsersIdentity:Crypto:OtpHmacKey must be at least 32 bytes (Base64).");
    }

    internal byte[] RequirePayloadKey()
    {
        var key = Decode(PayloadEncryptionKey, "PayloadEncryptionKey");
        return key.Length == 32 ? key : throw new InvalidOperationException("UsersIdentity:Crypto:PayloadEncryptionKey must be exactly 32 bytes (Base64).");
    }

    private static byte[] Decode(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"UsersIdentity:Crypto:{name} is not configured. Set it with user-secrets or an environment variable.");
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new InvalidOperationException($"UsersIdentity:Crypto:{name} must be Base64."); }
    }
}

/// <summary>Generates the password that is emailed after registration: random, 16 characters, all four character classes.</summary>
public sealed class SecurePasswordGenerator : IPasswordGenerator
{
    private const string Lower = "abcdefghijkmnopqrstuvwxyz", Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", Digits = "23456789", Symbols = "!@#$%^&*-_=+?";

    public string Generate()
    {
        var all = Lower + Upper + Digits + Symbols;
        var chars = new List<char> { Pick(Lower), Pick(Upper), Pick(Digits), Pick(Symbols) };
        while (chars.Count < 16) chars.Add(Pick(all));
        for (var i = chars.Count - 1; i > 0; i--)    // Fisher-Yates with a cryptographic source
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}

public sealed class OtpCodeService(IdentityCryptoOptions crypto, IdentitySecurityOptions options) : IOtpCodeService
{
    private readonly byte[] _key = crypto.RequireOtpKey();

    public string GenerateCode() =>
        RandomNumberGenerator.GetInt32((int)Math.Pow(10, options.OtpLength)).ToString().PadLeft(options.OtpLength, '0');

    public byte[] Hash(OtpPurpose purpose, string normalizedEmail, string code) =>
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{purpose}:{normalizedEmail}:{code}"));
}

public sealed class TokenService : ITokenService
{
    public string GenerateToken() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
    public byte[] GenerateNonce() => RandomNumberGenerator.GetBytes(32);
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>AES-256-GCM. Output layout: nonce (12) | tag (16) | ciphertext.</summary>
public sealed class AesGcmPayloadProtector(IdentityCryptoOptions crypto) : IPayloadProtector
{
    private readonly byte[] _key = crypto.RequirePayloadKey();

    public byte[] Protect(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plaintext.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plaintext, cipher, tag);
        return [.. nonce, .. tag, .. cipher];
    }

    public byte[] Unprotect(byte[] data)
    {
        if (data.Length < 28) throw new CryptographicException("The protected payload is too short.");
        var nonce = data.AsSpan(0, 12);
        var tag = data.AsSpan(12, 16);
        var cipher = data.AsSpan(28);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}

/// <summary>
/// Verifies the device's proof of possession: ECDSA P-256 / SHA-256, public key as DER SubjectPublicKeyInfo, signature as DER.
/// The Device PIN is verified by the device OS before the private key can sign; nothing here handles a PIN.
/// Optional hardware key attestation (Apple App Attest / Android Key Attestation) is not implemented.
/// </summary>
public sealed class EcdsaQuickLoginCrypto : IQuickLoginCrypto
{
    private const string NistP256Oid = "1.2.840.10045.3.1.7";

    public bool IsValidPublicKey(byte[] spki, string algorithm)
    {
        if (algorithm != KeyAlgorithms.Es256) return false;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spki, out var read);
            return read == spki.Length && ecdsa.ExportParameters(false).Curve.Oid.Value == NistP256Oid;
        }
        catch (CryptographicException) { return false; }
    }

    public bool VerifySignature(byte[] spki, string algorithm, byte[] data, byte[] signature)
    {
        if (algorithm != KeyAlgorithms.Es256) return false;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException) { return false; }
    }
}

/// <summary>
/// Placeholder until the Notifications module owns email delivery. It refuses to send (it never logs a code or password),
/// so the missing integration is explicit. Replace it by registering another IIdentityEmailSender.
/// </summary>
public sealed class UnconfiguredEmailSender : IIdentityEmailSender
{
    private static ExternalIntegrationException NotConfigured() =>
        new("Email", "No email delivery is configured. Register an IIdentityEmailSender (the Notifications module will provide it).");

    public Task SendOtpAsync(string email, OtpPurpose purpose, string code, TimeSpan lifetime, CancellationToken ct) => throw NotConfigured();
    public Task SendGeneratedPasswordAsync(string email, string password, CancellationToken ct) => throw NotConfigured();
}

/// <summary>
/// Placeholder: verifying Google / Apple ID tokens needs each provider's client ids and signing keys. Until that
/// integration is configured every social sign-in is refused instead of trusting an unverified token.
/// </summary>
public sealed class UnconfiguredExternalIdentityVerifier : IExternalIdentityVerifier
{
    public Task<ExternalIdentity?> VerifyAsync(ExternalProvider provider, string idToken, CancellationToken ct) =>
        throw new ExternalIntegrationException($"{provider}", $"{provider} sign-in is not configured. Register an IExternalIdentityVerifier with the provider's client ids.");
}
