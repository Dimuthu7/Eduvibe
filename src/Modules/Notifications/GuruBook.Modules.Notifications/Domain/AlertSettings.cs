using GuruBook.Shared.Domain;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Tenancy;

namespace GuruBook.Modules.Notifications.Domain;

/// <summary>
/// One topic a person has switched off for one child: payments, attendance or results. It belongs to the person who set it, not to a
/// teacher, because a parent's choice holds for every teacher their child learns with.
/// </summary>
public sealed class AlertMute : Entity
{
    /// <summary>The account that will not be told: a parent's sign-in id.</summary>
    public Guid UserId { get; set; }

    /// <summary>The child the silence is about.</summary>
    public Guid StudentId { get; set; }

    /// <summary>One of <see cref="AlertTopics"/>.</summary>
    public required string Topic { get; set; }
}
