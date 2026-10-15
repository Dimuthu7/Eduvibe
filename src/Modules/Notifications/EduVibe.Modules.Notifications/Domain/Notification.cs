using EduVibe.Shared.Domain;

namespace EduVibe.Modules.Notifications.Domain;

/// <summary>
/// Something a person is told: a student asked to join, results were published, a payment came in. Kind and ParamsJson say what; the app
/// writes the words, so they follow the person's language. It belongs to one person, so it is not tenant owned:
/// every query filters on the signed-in person's own ids.
/// </summary>
public sealed class Notification : Entity
{
    /// <summary>A student's sign-in account id, or a teacher's id (their tenant id).</summary>
    public Guid RecipientId { get; set; }
    public required string Kind { get; set; }

    /// <summary>The words that go into the message, as a JSON object (names, amounts, dates).</summary>
    public string ParamsJson { get; set; } = "{}";

    /// <summary>Where the app goes when it is tapped.</summary>
    public string? Link { get; set; }

    /// <summary>Set for something that must reach the person once: asking again changes nothing.</summary>
    public string? OnceKey { get; set; }

    /// <summary>Set for something that repeats: while one with this key is unread, the next joins it instead of adding a line.</summary>
    public string? GroupKey { get; set; }

    /// <summary>How many events this one stands for.</summary>
    public int Count { get; set; } = 1;

    public DateTimeOffset? ReadAt { get; set; }
}
