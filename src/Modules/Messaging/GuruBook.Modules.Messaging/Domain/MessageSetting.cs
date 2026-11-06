using GuruBook.Shared.Domain;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Tenancy;

namespace GuruBook.Modules.Messaging.Domain;

/// <summary>
/// What one teacher chose for one of the messages GuruBook sends on its own. A row is made the first time they change something, so a teacher
/// who never opens the Messages page has no rows and gets <see cref="MessageRule.Default"/> for everything.
/// </summary>
public sealed class MessageSetting : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>One of <see cref="MessageEvents"/>.</summary>
    public required string Event { get; set; }

    public bool On { get; set; } = true;

    /// <summary>One of <see cref="MessageWays"/>.</summary>
    public string Way { get; set; } = MessageWays.App;

    /// <summary>The teacher's own line at the end of their WhatsApp message, when they wrote one.</summary>
    public string? Note { get; set; }

    /// <summary>Only for <see cref="MessageEvents.LowAttendance"/>: the share of classes a child must attend to say nothing.</summary>
    public int? Percent { get; set; }
}

/// <summary>One teacher's own wording for one event's text message, in one language. Without a row, GuruBook's own words are sent.</summary>
public sealed class MessageText : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>One of <see cref="MessageEvents"/>.</summary>
    public required string Event { get; set; }

    /// <summary>"en", "si" or "ta".</summary>
    public required string Language { get; set; }

    /// <summary>The words, with the event's fields in braces: "{student} was absent from {class} on {date}."</summary>
    public required string Text { get; set; }
}
