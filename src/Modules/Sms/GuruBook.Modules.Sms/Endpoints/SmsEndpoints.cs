using GuruBook.Modules.Sms.Client;
using GuruBook.Modules.Sms.Domain;
using GuruBook.Modules.Sms.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Phone;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GuruBook.Modules.Sms.Endpoints;

/// <summary>
/// What the app needs to decide how to show SMS. Enabled is whether the teacher's plan includes it (false shows the locked state);
/// Configured is whether this server is set up to send; Simulated means it only pretends (a demo). Limit and Used count paid parts,
/// not messages, because that is what a text costs. Channel is how this teacher's paid messages go out.
/// </summary>
public sealed record SmsStatusDto(bool Enabled, bool Configured, bool Simulated, int? Limit, int Used, int? Remaining, string Channel);

public sealed record SmsMessageDto(
    Guid Id, string StudentName, string Kind, string Status, string? Error, string Phone, string Text, int Parts, bool Fallback,
    bool Stopped, DateTimeOffset CreatedAt);

public sealed record SmsListDto(SmsStatusDto Status, SmsMessageDto[] Items);

public sealed record ChannelRequest(string Channel);

public sealed record StopRequest(bool On);

/// <summary>Whether this person's own number is taking texts. For a parent or student in the app, about themselves.</summary>
public sealed record MyTextsDto(bool On);

/// <summary>The teacher's own SMS: whether it is available to them, what has been sent, and who asked not to be texted.</summary>
internal static class SmsEndpoints
{
    public const int Shown = 100;

    public static void Map(RouteGroupBuilder group)
    {
        // Status is open to every teacher so a Free teacher is shown the locked state instead of an error.
        group.MapGet("/status", Status).RequireAuthorization(Policies.Teacher);
        group.MapGet("/messages", Messages).RequireAuthorization(Policies.Teacher).RequireFeature(PlanFeatures.Sms);
        group.MapPut("/channel", SaveChannel).RequireAuthorization(Policies.Teacher).RequireFeature(PlanFeatures.Sms);
        group.MapPut("/messages/{id:guid}/stop", Stop).RequireAuthorization(Policies.Teacher).RequireFeature(PlanFeatures.Sms);

        // A parent or student saying "no more texts to my number", about their own number only.
        group.MapGet("/my-texts", MyTexts).RequireAuthorization();
        group.MapPut("/my-texts", SaveMyTexts).RequireAuthorization();
    }

    private static async Task<IResult> Status(
        SmsDbContext db, IPlanGate plans, ISmsClient client, ISmsSender sender, IOptions<SmsOptions> options, TimeProvider clock, CancellationToken ct) =>
        Results.Ok(await StatusAsync(db, plans, client, sender, options.Value, clock, ct));

    internal static async Task<SmsStatusDto> StatusAsync(
        SmsDbContext db, IPlanGate plans, ISmsClient client, ISmsSender sender, SmsOptions options, TimeProvider clock, CancellationToken ct)
    {
        var plan = await plans.CurrentAsync(ct);
        var enabled = plan.Has(PlanFeatures.Sms);
        int? limit = enabled ? plan.LimitOf(PlanLimits.SmsPerMonth) : 0;
        var month = clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM");
        var used = await db.Messages.Where(m => m.Month == month && m.Status != SmsStatus.Failed).SumAsync(m => m.Parts, ct);
        return new SmsStatusDto(
            enabled, client.IsConfigured, options.IsSimulated && client.IsConfigured, limit, used,
            limit is { } cap ? Math.Max(0, cap - used) : null, await sender.ChannelAsync(ct));
    }

    // Newest first. The id is time-ordered, so it sorts like the date and works on every database.
    // Opening the list also nudges the sender when something is still waiting, so a text never sits queued just because the server was asleep.
    private static async Task<IResult> Messages(
        SmsDbContext db, IPlanGate plans, ISmsClient client, ISmsSender sender, IOptions<SmsOptions> options, TimeProvider clock,
        SmsDispatchRunner runner, CancellationToken ct)
    {
        var rows = await db.Messages.AsNoTracking().OrderByDescending(m => m.Id).Take(Shown).ToListAsync(ct);
        if (rows.Any(m => m.Status == SmsStatus.Queued))
        {
            runner.Kick();
        }

        var phones = rows.Select(m => m.ToPhone).Distinct().ToList();
        var stopped = (await db.OptOuts.Where(o => phones.Contains(o.Phone)).Select(o => o.Phone).ToListAsync(ct)).ToHashSet();
        return Results.Ok(new SmsListDto(
            await StatusAsync(db, plans, client, sender, options.Value, clock, ct),
            [.. rows.Select(m => new SmsMessageDto(
                m.Id, m.StudentName, m.Kind, m.Status.ToString(), m.Error, Mask(m.ToPhone), m.Text, m.Parts, m.Fallback,
                stopped.Contains(m.ToPhone), m.CreatedAt))]));
    }

    private static async Task<IResult> SaveChannel(ChannelRequest request, SmsDbContext db, CancellationToken ct)
    {
        if (!MessageChannels.IsKnown(request.Channel))
        {
            return Problems.Reject("channel_invalid");
        }

        var row = await db.Settings.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            db.Settings.Add(new SmsSetting { Channel = request.Channel });
        }
        else
        {
            row.Channel = request.Channel;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new ChannelRequest(request.Channel));
    }

    /// <summary>A teacher stopping texts to a number they have already texted, because a parent asked them in person.</summary>
    private static async Task<IResult> Stop(Guid id, StopRequest request, SmsDbContext db, CancellationToken ct)
    {
        var phone = await db.Messages.Where(m => m.Id == id).Select(m => m.ToPhone).FirstOrDefaultAsync(ct);
        if (phone is null)
        {
            return Results.NotFound();
        }

        await SetAsync(db, phone, request.On, byOwner: false, ct);
        return Results.Ok(new StopRequest(request.On));
    }

    private static async Task<IResult> MyTexts(ITenantContext me, IUserDirectory users, SmsDbContext db, CancellationToken ct)
    {
        if (await MyPhoneAsync(me, users, ct) is not { } phone)
        {
            return Results.Ok(new MyTextsDto(true));
        }

        return Results.Ok(new MyTextsDto(!await db.OptOuts.AnyAsync(o => o.Phone == phone, ct)));
    }

    private static async Task<IResult> SaveMyTexts(MyTextsDto request, ITenantContext me, IUserDirectory users, SmsDbContext db, CancellationToken ct)
    {
        if (await MyPhoneAsync(me, users, ct) is not { } phone)
        {
            return Problems.Reject("no_phone");
        }

        await SetAsync(db, phone, request.On, byOwner: true, ct);
        return Results.Ok(new MyTextsDto(request.On));
    }

    private static async Task<string?> MyPhoneAsync(ITenantContext me, IUserDirectory users, CancellationToken ct) =>
        me.UserId is { } id ? PhoneNumber.Normalize(await users.PhoneAsync(id, ct)) : null;

    /// <summary>On means "text me": the row exists only while texts are stopped, so there is nothing to clean up afterwards.</summary>
    private static async Task SetAsync(SmsDbContext db, string phone, bool on, bool byOwner, CancellationToken ct)
    {
        var row = await db.OptOuts.FirstOrDefaultAsync(o => o.Phone == phone, ct);
        if (on && row is not null)
        {
            db.OptOuts.Remove(row);
        }
        else if (!on && row is null)
        {
            db.OptOuts.Add(new SmsOptOut { Phone = phone, ByOwner = byOwner });
        }
        else if (!on && row is not null)
        {
            row.ByOwner = byOwner;
        }

        await db.SaveChangesAsync(ct);
    }

    // The list shows only the end of the number: enough to tell parents apart, not enough to copy.
    private static string Mask(string phone) => phone.Length <= 4 ? phone : "•••• " + phone[^4..];
}
