using EduVibe.Shared.Domain;

namespace EduVibe.Modules.Classes.Domain;

/// <summary>A tuition centre or school where teachers hold classes. Institutes have no login in the MVP.</summary>
public sealed class Institute : Entity, IAuditable
{
    public required string Name { get; set; }
    public string? District { get; set; }
    public string? Town { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Links a teacher to an institute. TeacherId is a teacher's id from the Identity module.</summary>
public sealed class InstituteTeacher : Entity
{
    public Guid InstituteId { get; set; }
    public Guid TeacherId { get; set; }
}
