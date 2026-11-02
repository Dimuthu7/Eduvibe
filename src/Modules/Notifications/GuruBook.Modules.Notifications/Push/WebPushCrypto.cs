using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuruBook.Modules.Notifications.Push;

/// <summary>
/// The two pieces of web push that need cryptography, written against the standards so no extra library is needed:
/// encrypting a message for a device (RFC 8291, "aes128gcm" content coding of RFC 8188) and signing the sender's identity (VAPID, RFC 8292).
/// The encryption is checked against the example in RFC 8291 section 5 by the tests.
/// </summary>
internal static class WebPushCrypto
{
    private const int RecordSize = 4096;

    public static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    public static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> for one device into the body of a push request.
    /// <paramref name="senderKey"/> and <paramref name="salt"/> are made fresh for every message; they are parameters only so the tests can use the standard's example.
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] devicePublicKey, byte[] authSecret, ECDiffieHellman? senderKey = null, byte[]? salt = null)
    {
        using var generated = senderKey is null ? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256) : null;
        var sender = senderKey ?? generated!;
        salt ??= RandomNumberGenerator.GetBytes(16);

        var senderPublic = UncompressedPublicKey(sender);
        using var device = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = devicePublicKey[1..33], Y = devicePublicKey[33..65] },
        });
        var shared = sender.DeriveRawSecretAgreement(device.PublicKey);

        // Mix the device's auth secret into the shared secret, then derive the content key and nonce from a fresh salt.
        var prkKey = HKDF.Extract(HashAlgorithmName.SHA256, shared, authSecret);
        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), devicePublicKey, senderPublic);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var key = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        // One record: the message, then 0x02 to say it is the last record.
        var record = Concat(plaintext, [0x02]);
        if (record.Length > RecordSize - 16)
        {
            throw new ArgumentException("The message is too long for one push record.", nameof(plaintext));
        }

        var cipher = new byte[record.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, record, cipher, tag);
        }

        var header = new byte[16 + 4 + 1 + senderPublic.Length];
        salt.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), RecordSize);
        header[20] = (byte)senderPublic.Length;
        senderPublic.CopyTo(header, 21);
        return Concat(header, cipher, tag);
    }

    /// <summary>The "Authorization: vapid ..." header value that tells the push service who is sending, valid for 12 hours.</summary>
    public static string VapidAuthorization(string endpoint, string subject, string publicKey, string privateKey, DateTimeOffset now)
    {
        var uri = new Uri(endpoint);
        var audience = $"{uri.Scheme}://{uri.Authority}";
        var header = ToBase64Url(Encoding.UTF8.GetBytes("""{"typ":"JWT","alg":"ES256"}"""));
        var claims = ToBase64Url(JsonSerializer.SerializeToUtf8Bytes(new { aud = audience, exp = now.AddHours(12).ToUnixTimeSeconds(), sub = subject }));
        var signedPart = $"{header}.{claims}";

        var publicBytes = FromBase64Url(publicKey);
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = FromBase64Url(privateKey),
            Q = new ECPoint { X = publicBytes[1..33], Y = publicBytes[33..65] },
        });
        // ES256 wants the two halves of the signature side by side, which is what .NET produces by default.
        var signature = ecdsa.SignData(Encoding.ASCII.GetBytes(signedPart), HashAlgorithmName.SHA256);
        return $"vapid t={signedPart}.{ToBase64Url(signature)}, k={publicKey}";
    }

    private static byte[] UncompressedPublicKey(ECDiffieHellman key)
    {
        var point = key.ExportParameters(false).Q;
        return Concat([0x04], point.X!, point.Y!);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var at = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, at);
            at += part.Length;
        }

        return result;
    }
}
