using GuruBook.Shared.Auditing;
using GuruBook.Shared.Domain;
using GuruBook.Shared.Tenancy;

namespace GuruBook.Modules.Support.Domain;

/// <summary>Where a ticket has got to. The Super Admin moves it along; the teacher only sees it.</summary>
public enum TicketStatus
{
    /// <summary>Raised, nobody has picked it up yet.</summary>
    Open,

    /// <summary>Someone is on it.</summary>
    InProgress,

    /// <summary>Done, as far as support is concerned.</summary>
    Resolved,
}

/// <summary>What a teacher is reporting. The web app turns the key into words, so it reads in their own language.</summary>
public static class TicketKinds
{
    public const string Problem = "problem";
    public const string Question = "question";
    public const string Idea = "idea";
    public const string Billing = "billing";

    public static readonly IReadOnlyList<string> All = [Problem, Question, Idea, Billing];

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);
}

/// <summary>
/// One thing a teacher reported: what kind it is, what they wrote, and an optional screenshot. The conversation that follows lives in
/// <see cref="TicketReply"/>, so a ticket row never changes except for its status.
/// </summary>
public sealed class Ticket : Entity, ITenantOwned
{
    /// <summary>The teacher who raised it.</summary>
    public Guid TenantId { get; set; }

    /// <summary>One of <see cref="TicketKinds"/>.</summary>
    public required string Kind { get; set; }

    public required string Details { get; set; }

    /// <summary>The screen the teacher was on, when the app knew it. Saves asking "where were you?".</summary>
    public string? Screen { get; set; }

    public string? ShotContentType { get; set; }

    /// <summary>The screenshot itself, when they sent one. Kept out of the audit log, like any other picture.</summary>
    [NotAudited]
    public byte[]? Shot { get; set; }

    public TicketStatus Status { get; set; }

    /// <summary>When support last wrote back, so the teacher's list can show what is waiting on them.</summary>
    public DateTimeOffset? AnsweredAt { get; set; }
}

/// <summary>One message in a ticket's conversation, from the teacher or from support.</summary>
public sealed class TicketReply : Entity, ITenantOwned
{
    /// <summary>The teacher whose ticket this is, so their own rows go with them when they are erased.</summary>
    public Guid TenantId { get; set; }

    public Guid TicketId { get; set; }

    public required string Body { get; set; }

    /// <summary>True when the Super Admin wrote it.</summary>
    public bool FromSupport { get; set; }
}

/// <summary>The one row of support settings the Super Admin keeps. It belongs to nobody, like the rest of the catalog.</summary>
public sealed class SupportSettings : Entity
{
    /// <summary>The number a teacher taps to call, in international form.</summary>
    public string? Phone { get; set; }

    /// <summary>When that number is answered, in the Super Admin's own words ("9 AM to 6 PM, Monday to Saturday").</summary>
    public string? Hours { get; set; }
}
