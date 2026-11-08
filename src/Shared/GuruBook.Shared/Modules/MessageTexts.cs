using System.Text.RegularExpressions;

namespace GuruBook.Shared.Modules;

/// <summary>
/// The words of every message GuruBook sends to a phone, in English, Sinhala and Tamil. They live in code, and not in the web app's language
/// files like the rest of the app's words, because a text message is written where it is sent, and a teacher may rewrite any of them.
/// They are kept short on purpose: a phone fits 160 English letters in one paid part but only 70 Sinhala or Tamil ones, so an extra
/// sentence can double what a teacher pays. A field of the message's event goes in braces: "{student} was absent from {class}".
/// </summary>
public static partial class MessageTexts
{
    /// <summary>A text in Latin letters fits this many characters in one part.</summary>
    public const int GsmSingle = 160;

    /// <summary>And this many in each part once it needs more than one, because the parts carry a header.</summary>
    public const int GsmMulti = 153;

    /// <summary>A text with any Sinhala or Tamil letter is sent as Unicode, which fits far less.</summary>
    public const int UnicodeSingle = 70;
    public const int UnicodeMulti = 67;

    /// <summary>Nothing longer than this is sent, so one text can never cost more than four parts.</summary>
    public const int MaxLength = 600;

    /// <summary>A teacher's own line at the end of a WhatsApp message is kept to one short sentence.</summary>
    public const int MaxNoteLength = 80;

    public static readonly IReadOnlyList<string> Languages = ["en", "si", "ta"];

    private static readonly Dictionary<string, Dictionary<string, string>> Patterns = new()
    {
        [MessageEvents.FeeBefore] = new()
        {
            ["en"] = "{student}'s {class} fee for {month}, {amount}, is due tomorrow. - {teacher}, GuruBook",
            ["si"] = "{student}ගේ {class} පන්තියේ {month} ගාස්තුව {amount} හෙට ගෙවිය යුතුය. - {teacher}, GuruBook",
            ["ta"] = "{student} இன் {class} வகுப்பின் {month} கட்டணம் {amount} நாளை செலுத்த வேண்டும். - {teacher}, GuruBook",
        },
        [MessageEvents.FeeDueToday] = new()
        {
            ["en"] = "{student}'s {class} fee for {month}, {amount}, is due today. - {teacher}, GuruBook",
            ["si"] = "{student}ගේ {class} පන්තියේ {month} ගාස්තුව {amount} අද ගෙවිය යුතුය. - {teacher}, GuruBook",
            ["ta"] = "{student} இன் {class} வகுப்பின் {month} கட்டணம் {amount} இன்று செலுத்த வேண்டும். - {teacher}, GuruBook",
        },
        [MessageEvents.FeeOverdue] = new()
        {
            ["en"] = "{student}'s {class} fee for {month} is {amount}, still to be paid. - {teacher}, GuruBook",
            ["si"] = "{student}ගේ {class} පන්තියේ {month} ගාස්තුව {amount} තවම ගෙවා නැත. - {teacher}, GuruBook",
            ["ta"] = "{student} இன் {class} வகுப்பின் {month} கட்டணம் {amount} இன்னும் செலுத்தப்படவில்லை. - {teacher}, GuruBook",
        },
        [MessageEvents.PaymentReceived] = new()
        {
            ["en"] = "Received {amount} for {student}'s {class} fee. Receipt {receipt}. - {teacher}, GuruBook",
            ["si"] = "{student}ගේ {class} පන්තියේ ගාස්තුව සඳහා {amount} ලැබුණි. රිසිට්පත {receipt}. - {teacher}, GuruBook",
            ["ta"] = "{student} இன் {class} வகுப்புக் கட்டணமாக {amount} கிடைத்தது. ரசீது {receipt}. - {teacher}, GuruBook",
        },
        [MessageEvents.ClaimRejected] = new()
        {
            ["en"] = "The slip for {student}'s {class} fee for {month} was not accepted. Please check the app. - {teacher}, GuruBook",
            ["si"] = "{student}ගේ {class} පන්තියේ {month} ගාස්තු ගෙවීම් පත පිළිගත්තේ නැත. යෙදුම බලන්න. - {teacher}, GuruBook",
            ["ta"] = "{student} இன் {class} வகுப்பின் {month} கட்டணச் சீட்டு ஏற்கப்படவில்லை. செயலியைப் பாருங்கள். - {teacher}, GuruBook",
        },
        [MessageEvents.Absent] = new()
        {
            ["en"] = "{student} was absent from the {class} class on {date}. - {teacher}, GuruBook",
            ["si"] = "{student} {date} දින {class} පන්තියට පැමිණියේ නැත. - {teacher}, GuruBook",
            ["ta"] = "{student} {date} அன்று {class} வகுப்பிற்கு வரவில்லை. - {teacher}, GuruBook",
        },
        [MessageEvents.Arrived] = new()
        {
            ["en"] = "{student} arrived at the {class} class at {time}. - {teacher}, GuruBook",
            ["si"] = "{student} {class} පන්තියට {time} ට පැමිණියා. - {teacher}, GuruBook",
            ["ta"] = "{student} {class} வகுப்பிற்கு {time} க்கு வந்தார். - {teacher}, GuruBook",
        },
        [MessageEvents.LowAttendance] = new()
        {
            ["en"] = "{student} has attended only {percent} of the {class} classes in the last {weeks} weeks. - {teacher}, GuruBook",
            ["si"] = "{student} පසුගිය සති {weeks} තුළ {class} පන්තියට {percent} පමණක් පැමිණ ඇත. - {teacher}, GuruBook",
            ["ta"] = "{student} கடந்த {weeks} வாரங்களில் {class} வகுப்பிற்கு {percent} மட்டுமே வந்துள்ளார். - {teacher}, GuruBook",
        },
        [MessageEvents.ResultsPublished] = new()
        {
            ["en"] = "{student} got {marks} ({grade}), rank {rank}, in {exam} - {class}. - {teacher}, GuruBook",
            ["si"] = "{student}, {class} පන්තියේ {exam} විභාගයේ {marks} ({grade}), ශ්‍රේණිය {rank}. - {teacher}, GuruBook",
            ["ta"] = "{student}, {class} வகுப்பின் {exam} தேர்வில் {marks} ({grade}), தரம் {rank}. - {teacher}, GuruBook",
        },
    };

    /// <summary>Added to the first text a number ever gets, so a parent knows these can be stopped. Every later text leaves it off, to keep them cheap.</summary>
    private static readonly Dictionary<string, string> StopNote = new()
    {
        ["en"] = "To stop: ask your teacher.",
        ["si"] = "නැවැත්වීමට ගුරුවරයාට කියන්න.",
        ["ta"] = "நிறுத்த: ஆசிரியரிடம் கூறுங்கள்.",
    };

    /// <summary>The line a WhatsApp message ends with when the teacher has written none of their own.</summary>
    private static readonly Dictionary<string, string> PlainNote = new()
    {
        ["en"] = "Thank you.",
        ["si"] = "ස්තූතියි.",
        ["ta"] = "நன்றி.",
    };

    public static bool IsKnownLanguage(string? language) => language is not null && Languages.Contains(language);

    /// <summary>The language to write in: the teacher's, and English for anything else.</summary>
    public static string LanguageOf(string? language) => IsKnownLanguage(language) ? language! : "en";

    /// <summary>GuruBook's own words for this event, which a teacher's wording replaces.</summary>
    public static string Default(string messageEvent, string? language) =>
        Patterns[messageEvent][LanguageOf(language)];

    public static bool Has(string? messageEvent) => messageEvent is not null && Patterns.ContainsKey(messageEvent);

    public static string Note(string? note, string? language) =>
        string.IsNullOrWhiteSpace(note) ? PlainNote[LanguageOf(language)] : note.Trim();

    /// <summary>
    /// The words to send, with the event's values put in. <paramref name="pattern"/> is the teacher's own wording when they wrote one.
    /// <paramref name="withStopNote"/> adds the "ask your teacher" line.
    /// </summary>
    public static string Compose(string messageEvent, string? language, IReadOnlyList<string> values, bool withStopNote, string? pattern = null)
    {
        if (!Has(messageEvent))
        {
            throw new ArgumentException($"There is no text for '{messageEvent}'.", nameof(messageEvent));
        }

        var lang = LanguageOf(language);
        var text = Fill(pattern is { Length: > 0 } ? pattern : Default(messageEvent, lang), messageEvent, values);
        if (withStopNote)
        {
            // Every text ends with "GuruBook" and no full stop, so one is added here rather than running the two sentences together.
            text = $"{text}. {StopNote[lang]}";
        }

        return text.Length > MaxLength ? text[..MaxLength] : text;
    }

    /// <summary>The wording with its fields filled in, for the preview a teacher sees while they type.</summary>
    public static string Fill(string pattern, string messageEvent, IReadOnlyList<string> values)
    {
        var fields = MessageEvents.Of(messageEvent).Fields;
        var text = pattern;
        for (var i = 0; i < fields.Count; i++)
        {
            text = text.Replace($"{{{fields[i]}}}", Clean(i < values.Count ? values[i] : ""), StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>Why this wording cannot be saved, or null when it can: a field that event does not have, or nothing but spaces.</summary>
    public static string? Problem(string messageEvent, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "message_text_empty";
        }

        if (text.Length > MaxLength)
        {
            return "message_text_too_long";
        }

        var fields = MessageEvents.Of(messageEvent).Fields;
        return Placeholders().Matches(text).Select(m => m.Groups[1].Value).Any(name => !fields.Contains(name))
            ? "message_field_unknown"
            : null;
    }

    /// <summary>How many paid parts a text takes.</summary>
    public static int Parts(string text)
    {
        if (text.Length == 0)
        {
            return 1;
        }

        var unicode = text.Any(c => c > 127);
        var single = unicode ? UnicodeSingle : GsmSingle;
        var multi = unicode ? UnicodeMulti : GsmMulti;
        return text.Length <= single ? 1 : (text.Length + multi - 1) / multi;
    }

    /// <summary>An amount as a message writes it: "LKR 2,000".</summary>
    public static string Lkr(decimal amount) =>
        "LKR " + amount.ToString(amount == decimal.Truncate(amount) ? "N0" : "N2", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A fee month as a message writes it: "March 2027" from "2027-03".</summary>
    public static string MonthLabel(string month) =>
        DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            .ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A teacher may have typed anything into a name, and a text is one line, so each value is tidied first.</summary>
    public static string Clean(string? value)
    {
        var text = Spaces().Replace((value ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '), " ").Trim();
        return text.Length == 0 ? "-" : text.Length > 120 ? text[..120] : text;
    }

    [GeneratedRegex(" {2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\{([a-z]+)\}")]
    private static partial Regex Placeholders();
}
