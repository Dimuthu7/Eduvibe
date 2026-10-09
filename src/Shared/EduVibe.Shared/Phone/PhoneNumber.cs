namespace EduVibe.Shared.Phone;

/// <summary>
/// Phone numbers identify students, parents and teachers, so the same person must always produce the
/// same text. Numbers are stored in international form, for example +94771234567.
/// </summary>
public static class PhoneNumber
{
    private const string SriLankaCode = "94";

    /// <summary>Returns the international form, or null when the text is not a usable phone number.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();
        var hasPlus = trimmed.StartsWith('+');
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());

        if (trimmed.Any(c => !char.IsAsciiDigit(c) && c is not (' ' or '-' or '(' or ')' or '+' or '.')))
        {
            return null;
        }

        if (hasPlus)
        {
            return IsPlausible(digits) ? "+" + digits : null;
        }

        if (digits.StartsWith("00"))
        {
            digits = digits[2..];
            return IsPlausible(digits) ? "+" + digits : null;
        }

        if (digits.StartsWith('0') && digits.Length == 10)
        {
            return "+" + SriLankaCode + digits[1..];
        }

        if (digits.StartsWith(SriLankaCode) && digits.Length == 11)
        {
            return "+" + digits;
        }

        if (digits.Length == 9 && !digits.StartsWith('0'))
        {
            return "+" + SriLankaCode + digits;
        }

        return null;
    }

    private static bool IsPlausible(string digits) => digits.Length is >= 8 and <= 15 && digits[0] != '0';
}
