namespace GuruBook.Shared.Modules;

/// <summary>
/// The kinds of WhatsApp message there are. Each is one template approved by Meta, so <see cref="WhatsAppDraft.Params"/> must be given in the order
/// below; docs/whatsapp-setup.md has the template texts.
/// </summary>
public static class WhatsAppKinds
{
    /// <summary>Params: student first name, class title, month ("March 2027"), amount ("LKR 2,000"), teacher name.</summary>
    public const string FeeReminder = "fee_reminder";

    /// <summary>Params: student first name, class title, date ("Mon 15 Mar"), teacher name.</summary>
    public const string Absence = "absence_alert";

    /// <summary>Params: student first name, exam name, class title, marks ("78 / 100"), grade, rank ("3 of 24" or "-"), teacher name.</summary>
    public const string Results = "results";

    public static readonly IReadOnlyList<string> All = [FeeReminder, Absence, Results];

    private static readonly Dictionary<string, int> Counts = new()
    {
        [FeeReminder] = 5,
        [Absence] = 4,
        [Results] = 7,
    };

    public static bool IsKnown(string kind) => Counts.ContainsKey(kind);

    /// <summary>How many values this kind takes. The same facts go out by WhatsApp and by SMS, so both channels count on this.</summary>
    public static int ParamCount(string kind) => Counts[kind];
}

/// <summary>
/// One message a module wants sent about one student. The DedupKey says what the message is about ("fee:{enrolment}:{month}"): a message with the same
/// key that was already sent inside <see cref="Window"/> (forever when null) is not sent again.
/// </summary>
public sealed record WhatsAppDraft(Guid StudentId, string Kind, IReadOnlyList<string> Params, string DedupKey, TimeSpan? Window = null);

/// <summary>
/// What sending a batch would do. Ready is how many would go; the rest of Total is why the others would not.
/// Limit is the plan's messages per month (null for no cap), Used is how many this month already counted.
/// Channel says which way they would go, because a teacher who chose SMS sees different wording and a different allowance.
/// </summary>
public sealed record WhatsAppPreview(
    bool Configured, int Total, int Ready, int NoNumber, int OptedOut, int AlreadySent, int OverAllowance,
    int? Limit, int Used, IReadOnlyList<Guid> NoNumberStudentIds, string Channel = MessageChannels.WhatsAppThenSms);

/// <summary>Configured is false when WhatsApp is not set up on this server; then nothing was queued.</summary>
public sealed record WhatsAppSendResult(
    bool Configured, int Queued, int NoNumber, int OptedOut, int AlreadySent, int OverAllowance,
    string Channel = MessageChannels.WhatsAppThenSms);

/// <summary>
/// Sends WhatsApp messages for the current teacher. The WhatsApp module implements it; the modules that know who should be told (fees, attendance,
/// exams) build drafts and hand them over. Messages are queued and sent in the background, so a slow or failing WhatsApp never holds up the teacher.
/// </summary>
public interface IWhatsAppSender
{
    Task<WhatsAppPreview> PreviewAsync(IReadOnlyList<WhatsAppDraft> drafts, CancellationToken cancellationToken = default);

    Task<WhatsAppSendResult> SendAsync(IReadOnlyList<WhatsAppDraft> drafts, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a screen shows before the teacher confirms a send: the preview's counts and the names of the students who have no number to message.
/// Every module that offers a WhatsApp send returns this, so the web app has one preview dialog.
/// </summary>
public sealed record WhatsAppPreviewDto(
    bool Configured, int Total, int Ready, int NoNumber, int OptedOut, int AlreadySent, int OverAllowance, int? Limit, int Used, string[] NoNumberNames,
    string Channel = MessageChannels.WhatsAppThenSms)
{
    public static WhatsAppPreviewDto From(WhatsAppPreview preview, IReadOnlyDictionary<Guid, string> namesByStudent) => new(
        preview.Configured, preview.Total, preview.Ready, preview.NoNumber, preview.OptedOut, preview.AlreadySent, preview.OverAllowance,
        preview.Limit, preview.Used,
        [.. preview.NoNumberStudentIds.Select(id => namesByStudent.TryGetValue(id, out var name) ? name : "").Where(n => n != "")],
        preview.Channel);
}
