using GuruBook.Modules.Messaging.Domain;
using GuruBook.Modules.Messaging.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Messaging.Endpoints;

/// <summary>GuruBook's words for one event in one language, and the teacher's own when they wrote some.</summary>
public sealed record MessageTextDto(string Language, string Default, string? Custom);

/// <summary>
/// One of the messages that go out on their own: whether it is on, how it is sent, the teacher's WhatsApp line, the fields its words may use,
/// and the ways it can be sent (an event Meta has approved no template for cannot go by WhatsApp).
/// </summary>
public sealed record MessageEventDto(
    string Event, bool On, string Way, string? Note, int? Percent, string[] Ways, string[] Fields, MessageTextDto[] Texts);

public sealed record MessageSettingsDto(MessageEventDto[] Events, int LowAttendanceWeeks, int LowAttendanceQuietDays, int NoteMaxLength);

public sealed record SaveRuleRequest(bool? On, string? Way, string? Note, int? Percent);

public sealed record SaveTextRequest(string? Text);

/// <summary>
/// The Messages page: the teacher decides which alerts go out, how each one is sent, and what the paid ones say. Everything starts on and in
/// the app, so a teacher who never comes here pays for nothing. A Pro feature; a Free teacher's students get the app's own notifications.
/// </summary>
internal static class MessagingEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var mine = group.MapGroup("").RequireAuthorization(Policies.Teacher).RequireFeature(PlanFeatures.CustomMessages);
        mine.MapGet("", All);
        mine.MapPut("/{messageEvent}", SaveRule);
        mine.MapPut("/{messageEvent}/texts/{language}", SaveText);
    }

    private static async Task<IResult> All(MessagingDbContext db, ITeacherDirectory teachers, ITenantContext user, CancellationToken ct) =>
        Results.Ok(await ReadAsync(db, teachers, user, ct));

    private static async Task<IResult> SaveRule(
        string messageEvent, SaveRuleRequest request, MessagingDbContext db, ITeacherDirectory teachers, ITenantContext user, CancellationToken ct)
    {
        if (user.TenantId is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        if (!MessageEvents.IsKnown(messageEvent))
        {
            return Results.NotFound();
        }

        if (request.On is not { } on)
        {
            return Problems.Reject("message_rule_invalid");
        }

        var way = request.Way ?? MessageWays.App;
        if (!MessageWays.IsKnown(way) || (MessageWays.UsesWhatsApp(way) && MessageEvents.Of(messageEvent).WhatsAppKind is null))
        {
            return Problems.Reject("message_way_invalid");
        }

        var note = Problems.Clean(request.Note);
        if (note is not null && note.Length > MessageTexts.MaxNoteLength)
        {
            return Problems.Reject("message_note_too_long");
        }

        if (request.Percent is { } percent && messageEvent == MessageEvents.LowAttendance
            && (percent < LowAttendanceRule.MinPercent || percent > LowAttendanceRule.MaxPercent))
        {
            return Problems.Reject("message_percent_invalid");
        }

        var row = await db.Settings.FirstOrDefaultAsync(s => s.Event == messageEvent, ct);
        if (row is null)
        {
            row = new MessageSetting { TenantId = tenantId, Event = messageEvent };
            db.Settings.Add(row);
        }

        row.On = on;
        row.Way = way;
        row.Note = note;
        row.Percent = messageEvent == MessageEvents.LowAttendance ? request.Percent : null;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ReadAsync(db, teachers, user, ct));
    }

    // An empty text puts GuruBook's own words back.
    private static async Task<IResult> SaveText(
        string messageEvent, string language, SaveTextRequest request, MessagingDbContext db, ITeacherDirectory teachers,
        ITenantContext user, CancellationToken ct)
    {
        if (user.TenantId is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        if (!MessageEvents.IsKnown(messageEvent) || !MessageTexts.IsKnownLanguage(language))
        {
            return Results.NotFound();
        }

        var row = await db.Texts.FirstOrDefaultAsync(t => t.Event == messageEvent && t.Language == language, ct);
        var text = Problems.Clean(request.Text);
        if (text is null)
        {
            if (row is not null)
            {
                db.Texts.Remove(row);
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(await ReadAsync(db, teachers, user, ct));
        }

        if (MessageTexts.Problem(messageEvent, text) is { } problem)
        {
            return Problems.Reject(problem);
        }

        if (row is null)
        {
            db.Texts.Add(new MessageText { TenantId = tenantId, Event = messageEvent, Language = language, Text = text });
        }
        else
        {
            row.Text = text;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(await ReadAsync(db, teachers, user, ct));
    }

    private static async Task<MessageSettingsDto> ReadAsync(
        MessagingDbContext db, ITeacherDirectory teachers, ITenantContext user, CancellationToken ct)
    {
        var rules = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Event, ct);
        var texts = await db.Texts.AsNoTracking().ToListAsync(ct);
        var language = MessageTexts.LanguageOf(user.TenantId is { } me ? await teachers.LanguageAsync(me, ct) : "en");

        var events = MessageEvents.All.Select(name =>
        {
            var info = MessageEvents.Of(name);
            rules.TryGetValue(name, out var row);
            var ways = MessageWays.Choices.Where(w => info.WhatsAppKind is not null || !MessageWays.UsesWhatsApp(w)).ToArray();
            var wording = MessageTexts.Languages
                // The teacher's own language comes first, because it is the one they will write in.
                .Select((l, order) => (Language: l, Order: order))
                .OrderBy(l => l.Language == language ? 0 : 1).ThenBy(l => l.Order)
                .Select(l => l.Language)
                .Select(l => new MessageTextDto(l, MessageTexts.Default(name, l), texts.FirstOrDefault(t => t.Event == name && t.Language == l)?.Text))
                .ToArray();
            return new MessageEventDto(
                name, row?.On ?? MessageRule.Default.On, MessageWays.IsKnown(row?.Way) ? row!.Way : MessageRule.Default.Way, row?.Note,
                name == MessageEvents.LowAttendance ? MessageSettings.Percent(row?.Percent) : null, ways, [.. info.Fields], wording);
        }).ToArray();

        return new MessageSettingsDto(events, LowAttendanceRule.Weeks, LowAttendanceRule.QuietDays, MessageTexts.MaxNoteLength);
    }
}
