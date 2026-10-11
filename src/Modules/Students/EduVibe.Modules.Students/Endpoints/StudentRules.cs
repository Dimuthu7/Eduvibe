using EduVibe.Modules.Students.Domain;
using EduVibe.Shared.Phone;

namespace EduVibe.Modules.Students.Endpoints;

/// <summary>A student's details after the rules have been applied: trimmed text and phone numbers in international form.</summary>
internal sealed record CleanStudent(
    string Phone, string FirstName, string LastName, string? School, string? Grade, string? GuardianName, string? GuardianPhone);

internal static class StudentRules
{
    public const int MaxNameLength = 60;
    public const decimal MaxMoney = 10_000_000m;

    /// <summary>The phone is checked only when <paramref name="needPhone"/> is set, because an existing student's phone cannot change.</summary>
    public static (CleanStudent? Student, string? Error) Validate(
        string? phone, string? firstName, string? lastName, string? school, string? grade,
        string? guardianName, string? guardianPhone, bool needPhone)
    {
        var first = firstName?.Trim();
        var last = lastName?.Trim();
        if (string.IsNullOrEmpty(first) || first.Length > MaxNameLength)
        {
            return (null, "first_name_required");
        }

        if (string.IsNullOrEmpty(last) || last.Length > MaxNameLength)
        {
            return (null, "last_name_required");
        }

        var normalized = needPhone ? PhoneNumber.Normalize(phone) : "";
        if (normalized is null)
        {
            return (null, "phone_invalid");
        }

        string? guardianNormalized = null;
        if (Problems.Clean(guardianPhone) is { } rawGuardianPhone)
        {
            guardianNormalized = PhoneNumber.Normalize(rawGuardianPhone);
            if (guardianNormalized is null)
            {
                return (null, "guardian_phone_invalid");
            }
        }

        var schoolText = Problems.Clean(school);
        var gradeText = Problems.Clean(grade);
        var guardianText = Problems.Clean(guardianName);
        if (schoolText?.Length > 120 || gradeText?.Length > 30 || guardianText?.Length > 120)
        {
            return (null, "text_too_long");
        }

        return (new CleanStudent(normalized, first, last, schoolText, gradeText, guardianText, guardianNormalized), null);
    }

    public static FeePlan? ParseFeePlan(string? text) =>
        string.IsNullOrWhiteSpace(text) ? FeePlan.Full
        : Enum.TryParse<FeePlan>(text, ignoreCase: true, out var plan) && Enum.IsDefined(plan) ? plan : null;

    /// <summary>A discount needs an amount between zero and the class fee; other plans carry none.</summary>
    public static (decimal? Amount, string? Error) CheckDiscount(FeePlan plan, decimal? amount, decimal classFee)
    {
        if (plan != FeePlan.Discount)
        {
            return (0m, null);
        }

        return amount is > 0 && amount <= classFee && amount <= MaxMoney ? (amount, null) : (null, "discount_invalid");
    }
}
