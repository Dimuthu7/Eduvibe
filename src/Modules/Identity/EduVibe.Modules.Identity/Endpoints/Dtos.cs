namespace EduVibe.Modules.Identity.Endpoints;

public sealed record LoginRequest(string? Phone, string? Password);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record UpdateProfileRequest(
    string? FirstName, string? LastName, string? Email, string? Language,
    string? District, Guid? StreamId, Guid[]? SubjectIds);

public sealed record UserDto(
    Guid Id, string Phone, string FirstName, string LastName, string FullName, string? Email, string Language,
    string[] Roles, bool MustChangePassword, Guid? TeacherId, string? District, Guid? StreamId, Guid[] SubjectIds);

public sealed record SessionDto(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, UserDto User);

public sealed record CreateTeacherRequest(
    string? FirstName, string? LastName, string? Phone, string? Email,
    string? District, Guid? StreamId, Guid[]? SubjectIds);

public sealed record TeacherDto(
    Guid Id, string FirstName, string LastName, string FullName, string Phone, string? Email,
    string? District, Guid? StreamId, Guid[] SubjectIds, bool IsActive, bool MustChangePassword);

public sealed record TeacherCreatedDto(TeacherDto Teacher, string OneTimePassword);
public sealed record OneTimePasswordDto(string OneTimePassword);
public sealed record SetActiveRequest(bool IsActive);
