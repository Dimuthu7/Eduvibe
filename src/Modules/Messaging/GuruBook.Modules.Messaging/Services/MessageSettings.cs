using GuruBook.Shared.Modules;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Messaging.Services;

/// <summary>
/// What a teacher chose for the messages that go out on their own. A teacher with no row for an event gets the default: on, in the app.
/// A teacher whose plan does not include Message settings is read as the default too, so a plan that lapses stops the paid messages at once
/// without losing what they had chosen.
/// </summary>
internal sealed class MessageSettings(MessagingDbContext db, IPlanGate plans, ITenantContext tenant) : IMessageSettings
{
    public Task<MessageRule> RuleAsync(string messageEvent, CancellationToken ct = default) =>
        tenant.TenantId is { } me ? RuleForAsync(me, messageEvent, ct) : Task.FromResult(MessageRule.Default);

    public async Task<MessageRule> RuleForAsync(Guid teacherId, string messageEvent, CancellationToken ct = default)
    {
        if (!MessageEvents.IsKnown(messageEvent) || !await AllowedAsync(teacherId, ct))
        {
            return MessageRule.Default;
        }

        var row = await Rows(teacherId).FirstOrDefaultAsync(s => s.Event == messageEvent, ct);
        return row is null
            ? MessageRule.Default
            : new MessageRule(row.On, MessageWays.IsKnown(row.Way) ? row.Way : MessageWays.App, row.Note);
    }

    public async Task<string?> TextForAsync(Guid teacherId, string messageEvent, string language, CancellationToken ct = default)
    {
        if (!MessageEvents.IsKnown(messageEvent) || !await AllowedAsync(teacherId, ct))
        {
            return null;
        }

        return await db.Texts.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TenantId == teacherId && t.Event == messageEvent && t.Language == language)
            .Select(t => t.Text)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string?> NoteForAsync(Guid teacherId, string messageEvent, CancellationToken ct = default) =>
        (await RuleForAsync(teacherId, messageEvent, ct)).Note;

    public async Task<LowAttendanceRule> LowAttendanceForAsync(Guid teacherId, CancellationToken ct = default)
    {
        if (!await AllowedAsync(teacherId, ct))
        {
            return LowAttendanceRule.Default;
        }

        var row = await Rows(teacherId).FirstOrDefaultAsync(s => s.Event == MessageEvents.LowAttendance, ct);
        return row is null
            ? LowAttendanceRule.Default
            : new LowAttendanceRule(row.On, Percent(row.Percent));
    }

    /// <summary>A percentage that was stored before the limits changed is brought back inside them.</summary>
    internal static int Percent(int? value) => value is not { } percent
        ? LowAttendanceRule.DefaultPercent
        : Math.Clamp(percent, LowAttendanceRule.MinPercent, LowAttendanceRule.MaxPercent);

    // The background jobs act for nobody, so every read lifts the teacher filter and names the teacher outright.
    private IQueryable<Domain.MessageSetting> Rows(Guid teacherId) =>
        db.Settings.IgnoreQueryFilters().AsNoTracking().Where(s => s.TenantId == teacherId);

    private async Task<bool> AllowedAsync(Guid teacherId, CancellationToken ct) =>
        (await plans.ForTeacherAsync(teacherId, ct)).Has(PlanFeatures.CustomMessages);
}
