using EduVibe.Shared.Domain;

namespace EduVibe.Modules.Catalog.Domain;

/// <summary>What streams and subjects have in common, so one set of endpoints serves both.</summary>
public interface ICatalogItem
{
    string Name { get; set; }
    int SortOrder { get; set; }
    bool IsActive { get; set; }
}

/// <summary>A level or kind of teaching, such as O/L, A/L or Courses. The Super Admin can add more at any time.</summary>
public sealed class EducationStream : Entity, IAuditable, ICatalogItem
{
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A subject a teacher can teach. The Super Admin can add more at any time.</summary>
public sealed class Subject : Entity, IAuditable, ICatalogItem
{
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
