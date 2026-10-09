namespace EduVibe.Shared.Domain;

/// <summary>Base class for every stored entity: a time-ordered id plus created and updated timestamps.</summary>
public abstract class Entity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
