namespace EduVibe.Shared.Security;

public static class EduVibeClaims
{
    public const string Subject = "sub";
    public const string Role = "role";

    /// <summary>The teacher whose data this user works with. Set for teachers only.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>Present when the user signed in with a one-time password and must choose a new one.</summary>
    public const string MustChangePassword = "must_change_password";
}
