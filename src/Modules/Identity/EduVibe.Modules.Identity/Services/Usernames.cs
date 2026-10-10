using System.Text.RegularExpressions;

namespace EduVibe.Modules.Identity.Services;

/// <summary>
/// Usernames are what people sign in with after their first sign-in. They are not case sensitive, so
/// "Nimal.P" and "nimal.p" are the same name; the lowercase form is what is stored and compared.
/// </summary>
public static partial class Usernames
{
    public const int MinLength = 4;
    public const int MaxLength = 30;

    /// <summary>Lowercase, trimmed form, or null when the text is not an allowed username.</summary>
    public static string? Normalize(string? input)
    {
        var name = input?.Trim().ToLowerInvariant();
        return name is not null && Pattern().IsMatch(name) && name.Any(char.IsAsciiLetter) ? name : null;
    }

    // Letters, digits, dot, underscore and hyphen; starts with a letter or digit; must contain a letter
    // so a username can never be mistaken for a phone number.
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{3,29}$")]
    private static partial Regex Pattern();
}
