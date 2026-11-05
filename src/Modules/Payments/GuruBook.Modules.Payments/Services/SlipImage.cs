namespace GuruBook.Modules.Payments.Services;

/// <summary>Reading an uploaded image: a slip photo or a LankaQR. Only the three types a phone camera produces are taken.</summary>
internal static class SlipImage
{
    /// <summary>A slip is a photo of a piece of paper, so it is allowed to be bigger than a profile picture but not unbounded.</summary>
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
    public static async Task<byte[]?> ReadLimitedAsync(Stream body, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[32 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
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
