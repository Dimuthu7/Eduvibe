using GuruBook.Shared.Images;

namespace GuruBook.Modules.Payments.Services;

/// <summary>Reading an uploaded image: a slip photo or a LankaQR. The rules are the same everywhere, so they live in Shared.</summary>
internal static class SlipImage
{
    /// <summary>A slip is a photo of a piece of paper, so it is allowed to be bigger than a profile picture but not unbounded.</summary>
    public const int MaxBytes = ImageUpload.MaxBytes;

    /// <summary>The image type from the first bytes (JPEG, PNG or WebP), or null for anything else.</summary>
    public static string? TypeOf(ReadOnlySpan<byte> bytes) => ImageUpload.TypeOf(bytes);

    /// <summary>The whole body, or null when it is longer than <see cref="MaxBytes"/>.</summary>
    public static Task<byte[]?> ReadLimitedAsync(Stream body, CancellationToken ct) => ImageUpload.ReadLimitedAsync(body, ct);
}
