using System.Text.RegularExpressions;
using GuruBook.Shared.Modules;

namespace GuruBook.Modules.Sms.Services;

/// <summary>
/// The words of every text GuruBook sends, in English, Sinhala and Tamil. They live here, and not in the web app's language files like the rest
/// of the app's words, because an SMS is written where it is sent. They are kept short on purpose: a phone fits 160 English letters in one paid
/// part but only 70 Sinhala or Tamil ones, so an extra sentence can double what a teacher pays.
/// </summary>
public static partial class SmsTexts
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

    public static readonly IReadOnlyList<string> Languages = ["en", "si", "ta"];

    // {0} student, {1} class, {2} month, {3} amount, {4} teacher
    private static readonly Dictionary<string, string> Fee = new()
    {
        ["en"] = "{0}'s {1} fee for {2} is {3}, still to be paid. - {4}, GuruBook",
        ["si"] = "{0}ගේ {1} පන්තියේ {2} ගාස්තුව {3} තවම ගෙවා නැත. - {4}, GuruBook",
        ["ta"] = "{0} இன் {1} வகுப்பின் {2} கட்டணம் {3} இன்னும் செலுத்தப்படவில்லை. - {4}, GuruBook",
    };

    // {0} student, {1} class, {2} date, {3} teacher
    private static readonly Dictionary<string, string> Absence = new()
    {
        ["en"] = "{0} was absent from the {1} class on {2}. - {3}, GuruBook",
        ["si"] = "{0} {2} දින {1} පන්තියට පැමිණියේ නැත. - {3}, GuruBook",
        ["ta"] = "{0} {2} அன்று {1} வகுப்பிற்கு வரவில்லை. - {3}, GuruBook",
    };

    // {0} student, {1} exam, {2} class, {3} marks, {4} grade, {5} rank, {6} teacher
    private static readonly Dictionary<string, string> Results = new()
    {
        ["en"] = "{0} got {3} ({4}), rank {5}, in {1} - {2}. - {6}, GuruBook",
        ["si"] = "{0}, {2} පන්තියේ {1} විභාගයේ {3} ({4}), ශ්‍රේණිය {5}. - {6}, GuruBook",
        ["ta"] = "{0}, {2} வகுப்பின் {1} தேர்வில் {3} ({4}), தரம் {5}. - {6}, GuruBook",
    };

    /// <summary>Added to the first text a number ever gets, so a parent knows these can be stopped. Every later text leaves it off, to keep them cheap.</summary>
    private static readonly Dictionary<string, string> StopNote = new()
    {
        ["en"] = "To stop: ask your teacher.",
        ["si"] = "නැවැත්වීමට ගුරුවරයාට කියන්න.",
        ["ta"] = "நிறுத்த: ஆசிரியரிடம் கூறுங்கள்.",
    };

    public static bool IsKnownLanguage(string? language) => language is not null && Languages.Contains(language);

    /// <summary>The language to write in: the teacher's, and English for anything else.</summary>
    public static string LanguageOf(string? language) => IsKnownLanguage(language) ? language! : "en";

    public static bool IsKnownKind(string kind) => Texts(kind) is not null;

    /// <summary>The words to send, with the values put in. <paramref name="withStopNote"/> adds the "ask your teacher" line.</summary>
    public static string Compose(string kind, string? language, IReadOnlyList<string> values, bool withStopNote)
    {
        var lang = LanguageOf(language);
        var pattern = (Texts(kind) ?? throw new ArgumentException($"There is no SMS for '{kind}'.", nameof(kind)))[lang];
        var text = string.Format(pattern, [.. values.Select(Clean)]);
        if (withStopNote)
        {
            // Every text ends with "GuruBook" and no full stop, so one is added here rather than running the two sentences together.
            text = $"{text}. {StopNote[lang]}";
        }

        return text.Length > MaxLength ? text[..MaxLength] : text;
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

    /// <summary>A teacher may have typed anything into a name, and a text is one line, so each value is tidied first.</summary>
    public static string Clean(string? value)
    {
        var text = Spaces().Replace((value ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '), " ").Trim();
        return text.Length == 0 ? "-" : text.Length > 120 ? text[..120] : text;
    }

    private static Dictionary<string, string>? Texts(string kind) => kind switch
    {
        WhatsAppKinds.FeeReminder => Fee,
        WhatsAppKinds.Absence => Absence,
        WhatsAppKinds.Results => Results,
        _ => null,
    };

    [GeneratedRegex(" {2,}")]
    private static partial Regex Spaces();
}
