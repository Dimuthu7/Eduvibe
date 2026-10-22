using System.Text.RegularExpressions;
using GuruBook.Shared.Modules;

namespace GuruBook.Modules.WhatsApp.Services;

/// <summary>
/// The templates Meta must approve before anything can be sent. A message kind is one template; the body takes that many values, in order
/// (see <see cref="WhatsAppKinds"/>). The texts to submit are in docs/whatsapp-setup.md.
/// </summary>
internal static partial class WhatsAppTemplates
{
    private static readonly Dictionary<string, int> ParamCounts = new()
    {
        [WhatsAppKinds.FeeReminder] = 5,
        [WhatsAppKinds.Absence] = 4,
        [WhatsAppKinds.Results] = 7,
    };

    public static bool IsKnown(string kind) => ParamCounts.ContainsKey(kind);

    public static int ParamCount(string kind) => ParamCounts[kind];

    public static string NameOf(string prefix, string kind) => prefix + kind;

    /// <summary>
    /// Meta refuses empty values, new lines, tabs and runs of four spaces inside a value, and values over 1024 characters,
    /// and a teacher may have typed any of them into a name, so each value is tidied before it is sent.
    /// </summary>
    public static string Clean(string? value)
    {
        var text = Spaces().Replace((value ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '), " ").Trim();
        return text.Length == 0 ? "-" : text.Length > 200 ? text[..200] : text;
    }

    [GeneratedRegex(" {2,}")]
    private static partial Regex Spaces();
}
