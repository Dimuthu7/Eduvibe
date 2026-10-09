using System.Security.Cryptography;
using EduVibe.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace EduVibe.Modules.Identity.Services;

public sealed class PasswordService
{
    public const int MinimumLength = 8;

    // No 0/O, 1/l/I: one-time passwords are read out over the phone or typed from a message.
    private const string OtpAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;

    public static string NewOneTimePassword() => RandomNumberGenerator.GetString(OtpAlphabet, 10);
}

public static class TokenText
{
    public static string NewRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
