namespace GuruBook.Shared.Modules;

/// <summary>
/// Everything GuruBook tells a student or a parent on its own, without the teacher pressing anything. A teacher switches each one off and
/// chooses how it goes out; the words of the paid ones can be rewritten. The keys are stored, so they never change.
/// </summary>
public static class MessageEvents
{
    /// <summary>The day before a fee's month starts.</summary>
    public const string FeeBefore = "fee_before";

    /// <summary>On the day the fee is due.</summary>
    public const string FeeDueToday = "fee_due_today";

    /// <summary>Every week while a fee is still unpaid.</summary>
    public const string FeeOverdue = "fee_overdue";

    /// <summary>A payment recorded, by hand or by confirming a bank transfer.</summary>
    public const string PaymentReceived = "payment_received";

    /// <summary>A bank transfer the teacher did not accept.</summary>
    public const string ClaimRejected = "claim_rejected";

    /// <summary>A child was not in class.</summary>
    public const string Absent = "absent";

    /// <summary>A child arrived.</summary>
    public const string Arrived = "arrived";

    /// <summary>A child's attendance has fallen below what the teacher expects.</summary>
    public const string LowAttendance = "low_attendance";

    /// <summary>Exam results published.</summary>
    public const string ResultsPublished = "results_published";

    /// <summary>
    /// What each event is: the WhatsApp template it can use (none for an event Meta has not approved a template for), whether it can go by
    /// text message, and the fields its words may use, in the order the values are handed over.
    /// </summary>
    public sealed record Info(string Event, string? WhatsAppKind, IReadOnlyList<string> Fields);

    private static readonly Info[] Known =
    [
        new(FeeBefore, WhatsAppKinds.FeeReminder, ["student", "class", "month", "amount", "teacher"]),
        new(FeeDueToday, WhatsAppKinds.FeeReminder, ["student", "class", "month", "amount", "teacher"]),
        new(FeeOverdue, WhatsAppKinds.FeeReminder, ["student", "class", "month", "amount", "teacher"]),
        new(PaymentReceived, WhatsAppKinds.PaymentReceived, ["student", "class", "amount", "receipt", "teacher"]),
        new(ClaimRejected, null, ["student", "class", "month", "teacher"]),
        new(Absent, WhatsAppKinds.Absence, ["student", "class", "date", "teacher"]),
        new(Arrived, null, ["student", "class", "time", "teacher"]),
        new(LowAttendance, WhatsAppKinds.LowAttendance, ["student", "class", "percent", "weeks", "teacher"]),
        new(ResultsPublished, WhatsAppKinds.Results, ["student", "exam", "class", "marks", "grade", "rank", "teacher"]),
    ];

    private static readonly Dictionary<string, Info> ByEvent = Known.ToDictionary(i => i.Event);

    public static readonly IReadOnlyList<string> All = [.. Known.Select(i => i.Event)];

    public static bool IsKnown(string? messageEvent) => messageEvent is not null && ByEvent.ContainsKey(messageEvent);

    public static Info Of(string messageEvent) => ByEvent[messageEvent];

    /// <summary>The event a message of this kind belongs to when a teacher sends it by hand, so the wording they chose is used there too.</summary>
    public static string ForKind(string kind) => kind switch
    {
        WhatsAppKinds.Absence => Absent,
        WhatsAppKinds.Results => ResultsPublished,
        WhatsAppKinds.PaymentReceived => PaymentReceived,
        WhatsAppKinds.LowAttendance => LowAttendance,
        _ => FeeOverdue,
    };
}

/// <summary>How an alert reaches a parent. Only the app costs nothing, so it is where every event starts.</summary>
public static class MessageWays
{
    /// <summary>In the app and on the phone's lock screen, and nothing paid for.</summary>
    public const string App = "app";

    /// <summary>WhatsApp as well, and nothing else when WhatsApp cannot deliver it.</summary>
    public const string WhatsApp = "whatsapp";

    /// <summary>A text message as well.</summary>
    public const string Sms = "sms";

    /// <summary>WhatsApp as well, and a text message to the numbers WhatsApp could not reach.</summary>
    public const string WhatsAppThenSms = "whatsapp_then_sms";

    public static readonly IReadOnlyList<string> Choices = [App, WhatsApp, Sms, WhatsAppThenSms];

    public static bool IsKnown(string? way) => way is not null && Choices.Contains(way);

    /// <summary>True when this way sends something the teacher pays for.</summary>
    public static bool IsPaid(string way) => way != App;

    /// <summary>True when WhatsApp is tried, which an event without an approved template cannot do.</summary>
    public static bool UsesWhatsApp(string way) => way is WhatsApp or WhatsAppThenSms;
}

/// <summary>One event's setting: whether it goes out at all, how, and the teacher's own line at the end of the WhatsApp message.</summary>
public sealed record MessageRule(bool On, string Way, string? Note = null)
{
    /// <summary>Everything is on and in the app, so a teacher who never opens the Messages page pays for nothing and misses nothing.</summary>
    public static readonly MessageRule Default = new(true, MessageWays.App);
}

/// <summary>When parents hear that a child is missing classes: below this share of the classes held in the last few weeks.</summary>
public sealed record LowAttendanceRule(bool On, int Percent)
{
    public const int DefaultPercent = 75;
    public const int MinPercent = 30;
    public const int MaxPercent = 95;

    /// <summary>How far back attendance is counted.</summary>
    public const int Weeks = 4;

    /// <summary>A child's parent hears this at most once in this many days, however many classes are missed.</summary>
    public const int QuietDays = 14;

    public static readonly LowAttendanceRule Default = new(true, DefaultPercent);
}

/// <summary>One message about one student, in the values its event's fields take. The router turns it into WhatsApp or a text message.</summary>
public sealed record MessageDraft(Guid StudentId, IReadOnlyList<string> Params, string DedupKey, TimeSpan? Window = null);

/// <summary>
/// What a teacher has chosen for the messages that go out on their own. The Messaging module implements it; the SMS and WhatsApp modules ask it
/// for the wording, and <see cref="IMessageRouter"/> asks it what may go where.
/// </summary>
public interface IMessageSettings
{
    /// <summary>One event's setting for the signed-in teacher.</summary>
    Task<MessageRule> RuleAsync(string messageEvent, CancellationToken cancellationToken = default);

    /// <summary>One event's setting for a teacher named outright, for the background jobs, which act for nobody.</summary>
    Task<MessageRule> RuleForAsync(Guid teacherId, string messageEvent, CancellationToken cancellationToken = default);

    /// <summary>The teacher's own wording for a text message, or null to use GuruBook's.</summary>
    Task<string?> TextForAsync(Guid teacherId, string messageEvent, string language, CancellationToken cancellationToken = default);

    /// <summary>The line the teacher wants at the end of their WhatsApp messages, or null for GuruBook's.</summary>
    Task<string?> NoteForAsync(Guid teacherId, string messageEvent, CancellationToken cancellationToken = default);

    /// <summary>When this teacher's parents hear about a child missing classes.</summary>
    Task<LowAttendanceRule> LowAttendanceForAsync(Guid teacherId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sends an event's paid messages the way the teacher chose, so a module that knows who should be told needs to know nothing about channels.
/// The Messaging module implements it. Nothing is sent for an event that is off or set to the app.
/// </summary>
public interface IMessageRouter
{
    /// <summary>What the signed-in teacher chose for this event.</summary>
    Task<MessageRule> RuleAsync(string messageEvent, CancellationToken cancellationToken = default);

    /// <summary>What a teacher named outright chose, for a background job.</summary>
    Task<MessageRule> RuleForAsync(Guid teacherId, string messageEvent, CancellationToken cancellationToken = default);

    /// <summary>Sends for the signed-in teacher. Returns how many messages were queued.</summary>
    Task<int> SendAsync(string messageEvent, IReadOnlyList<MessageDraft> drafts, CancellationToken cancellationToken = default);

    /// <summary>Sends for a teacher named outright, from a background job.</summary>
    Task<int> SendForAsync(Guid teacherId, string messageEvent, IReadOnlyList<MessageDraft> drafts, CancellationToken cancellationToken = default);

    /// <summary>When the signed-in teacher's parents hear that a child is missing classes.</summary>
    Task<LowAttendanceRule> LowAttendanceAsync(CancellationToken cancellationToken = default);
}
