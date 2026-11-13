namespace GuruBook.Shared.Images;

/// <summary>
/// Reading a picture somebody sent from their phone: a bank slip, a LankaQR, a screenshot of something that went wrong. Only the three
/// types a phone produces are taken, and the type is read from the first bytes rather than believed from the request.
/// </summary>
public static class ImageUpload
{
    /// <summary>A photo of a piece of paper or a whole screen, so bigger than a profile picture but not unbounded.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>The image type from the first bytes (JPEG, PNG or WebP), or null for anything else.</summary>
    public static string? TypeOf(ReadOnlySpan<byte> b)
    {
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (b.Length > 8 && b[..8].SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (b.Length > 12 && b[..4].SequenceEqual<byte>("RIFF"u8) && b[8..12].SequenceEqual<byte>("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    /// <summary>The whole body, or null when it is longer than <see cref="MaxBytes"/>.</summary>
    public static async Task<byte[]?> ReadLimitedAsync(Stream body, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[32 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
