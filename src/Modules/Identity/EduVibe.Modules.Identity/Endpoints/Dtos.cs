namespace EduVibe.Modules.Identity.Endpoints;

public sealed record LoginRequest(string? Phone, string? Password);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record UpdateProfileRequest(string? FullName, string? Email, string? Language, string? Town, string? Subjects);

public sealed record UserDto(
    Guid Id, string Phone, string FullName, string? Email, string Language,
    string[] Roles, bool MustChangePassword, Guid? TeacherId, string? Town, string? Subjects);

public sealed record SessionDto(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, UserDto User);

public sealed record CreateTeacherRequest(string? FullName, string? Phone, string? Email, string? Town, string? Subjects);
public sealed record TeacherDto(Guid Id, string FullName, string Phone, string? Email, string? Town, string? Subjects, bool IsActive, bool MustChangePassword);
public sealed record TeacherCreatedDto(TeacherDto Teacher, string OneTimePassword);
public sealed record OneTimePasswordDto(string OneTimePassword);
public sealed record SetActiveRequest(bool IsActive);
