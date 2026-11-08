namespace GuruBook.Shared.Modules;

/// <summary>
/// How a teacher's paid messages go out. WhatsApp costs a fraction of an SMS, so it is tried first by default and SMS only steps in for a
/// parent who is not on WhatsApp; a teacher whose parents are mostly not on WhatsApp can choose SMS for everything instead.
/// </summary>
public static class MessageChannels
{
    /// <summary>WhatsApp, and an SMS to the numbers WhatsApp could not deliver to. The default.</summary>
    public const string WhatsAppThenSms = "whatsapp_then_sms";

    /// <summary>SMS only; nothing goes to WhatsApp.</summary>
    public const string SmsOnly = "sms_only";

    public static readonly IReadOnlyList<string> Choices = [WhatsAppThenSms, SmsOnly];

    public static bool IsKnown(string? channel) => channel is not null && Choices.Contains(channel);
}

/// <summary>
/// One SMS about one student. Kind and Params are a <see cref="WhatsAppDraft"/>'s, so the same facts can go out either way; the SMS module turns
/// them into words. The DedupKey says what the message is about: a message with the same key already sent inside <see cref="Window"/>
/// (forever when null) is not sent again, by either channel. <see cref="Event"/> is the <see cref="MessageEvents"/> this message belongs to,
/// which decides whose wording is used; left out, it is the one that kind belongs to.
/// </summary>
public sealed record SmsDraft(
    Guid StudentId, string Kind, IReadOnlyList<string> Params, string DedupKey, TimeSpan? Window = null, string? Event = null);

/// <summary>
/// What sending a batch of SMS would do. Ready is how many would go; the rest of Total is why the others would not. Parts is what those
/// Ready messages would cost, because Sinhala and Tamil fit about 70 letters in a part against 160 for English, and the allowance counts parts.
/// </summary>
public sealed record SmsPreview(
    bool Configured, int Total, int Ready, int Parts, int NoNumber, int OptedOut, int AlreadySent, int OverAllowance,
    int? Limit, int Used, IReadOnlyList<Guid> NoNumberStudentIds);

/// <summary>Configured is false when SMS is not set up on this server; then nothing was queued.</summary>
public sealed record SmsSendResult(bool Configured, int Queued, int NoNumber, int OptedOut, int AlreadySent, int OverAllowance);

/// <summary>
/// Sends SMS for the current teacher. The Sms module implements it; the modules that know who should be told hand over drafts, and the WhatsApp
/// module asks it to take over a message WhatsApp could not deliver. Messages are queued and sent in the background.
/// </summary>
public interface ISmsSender
{
    /// <summary>How this teacher's paid messages go out: one of <see cref="MessageChannels"/>.</summary>
    Task<string> ChannelAsync(CancellationToken cancellationToken = default);

    Task<SmsPreview> PreviewAsync(IReadOnlyList<SmsDraft> drafts, CancellationToken cancellationToken = default);

    Task<SmsSendResult> SendAsync(IReadOnlyList<SmsDraft> drafts, CancellationToken cancellationToken = default);

    /// <summary>Queues messages for a teacher named outright, for a background job, which acts for nobody.</summary>
    Task<SmsSendResult> SendForAsync(Guid teacherId, IReadOnlyList<SmsDraft> drafts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues one SMS for a teacher named outright, for the background sender, which acts for nobody. Returns false when that teacher's
    /// channel, plan, allowance or the number itself says no. The draft's own dedup key is not consulted: the caller already decided.
    /// </summary>
    Task<bool> TakeOverAsync(Guid teacherId, SmsDraft draft, string toPhone, string studentName, CancellationToken cancellationToken = default);
}
