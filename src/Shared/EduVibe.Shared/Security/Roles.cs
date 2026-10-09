namespace EduVibe.Shared.Security;

/// <summary>The roles EduVibe knows. All five exist from day one; only some have screens yet.</summary>
public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Teacher = "Teacher";
    public const string InstituteAdmin = "InstituteAdmin";
    public const string Student = "Student";
    public const string Parent = "Parent";

    public static readonly string[] All = [SuperAdmin, Teacher, InstituteAdmin, Student, Parent];
}
