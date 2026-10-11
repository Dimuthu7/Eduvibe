namespace EduVibe.Shared.Security;

public static class Policies
{
    /// <summary>Signed in, and past the forced password change. This is the default for every endpoint.</summary>
    public const string Active = "Active";

    /// <summary>Signed in, even if the one-time password has not been replaced yet.</summary>
    public const string SignedIn = "SignedIn";

    public const string SuperAdmin = "SuperAdmin";
    public const string Teacher = "Teacher";

    /// <summary>A student account: the screens about their own classes, attendance and results.</summary>
    public const string Student = "Student";
}
