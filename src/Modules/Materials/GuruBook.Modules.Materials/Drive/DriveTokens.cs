using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace GuruBook.Modules.Materials.Drive;

/// <summary>
/// Keeps a teacher's Google refresh token safe at rest (AES-GCM) and signs the short-lived state that carries a teacher through
/// Google's sign-in page and back. Both keys come from the API's signing key, so nothing new has to be set up on the host. Changing that
/// signing key makes stored tokens unreadable, and teachers then connect Drive again.
/// </summary>
public sealed class DriveTokens
{
    private readonly byte[] _encryptionKey;
    private readonly byte[] _stateKey;

    public DriveTokens(IConfiguration configuration)
    {
        var root = Encoding.UTF8.GetBytes(configuration["Jwt:SigningKey"] ?? "");
        _encryptionKey = HMACSHA256.HashData(root, "eduvibe-drive-token-encryption"u8);
        _stateKey = HMACSHA256.HashData(root, "eduvibe-drive-oauth-state"u8);
    }

    public string Protect(string token)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(token);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    /// <summary>The token, or null when the stored text cannot be read (the signing key changed, or it was damaged).</summary>
    public string? TryUnprotect(string stored)
    {
        try
        {
            var bytes = Convert.FromBase64String(stored);
            if (bytes.Length < 28)
            {
                return null;
            }

            var plain = new byte[bytes.Length - 28];
            using var aes = new AesGcm(_encryptionKey, 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }

    /// <summary>State for one visit to Google: who is connecting, good for ten minutes.</summary>
    public string SignState(Guid tenantId, DateTimeOffset now)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new StatePayload(tenantId, now.AddMinutes(10).ToUnixTimeSeconds(), Guid.NewGuid().ToString("N")));
        var mac = HMACSHA256.HashData(_stateKey, payload);
        return $"{Base64Url(payload)}.{Base64Url(mac)}";
    }

    /// <summary>The teacher in the state, or null when it was not signed by this server, was changed or has expired.</summary>
    public Guid? ReadState(string? state, DateTimeOffset now)
    {
        var parts = state?.Split('.');
        if (parts is not { Length: 2 })
        {
            return null;
        }

        try
        {
            var payload = FromBase64Url(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_stateKey, payload), FromBase64Url(parts[1])))
            {
                return null;
            }

            var read = JsonSerializer.Deserialize<StatePayload>(payload);
            return read is not null && read.Expires >= now.ToUnixTimeSeconds() ? read.Tenant : null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    private sealed record StatePayload(Guid Tenant, long Expires, string Nonce);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }
}
