using EduVibe.Modules.Plans.Domain;
using EduVibe.Shared.Plans;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Plans.Services;

/// <summary>The plans a fresh database starts with, and what a teacher gets if the default plan has gone missing.</summary>
public static class PlanDefaults
{
    public const string FreeName = "Free";
    public const string ProName = "Pro";

    /// <summary>Free has every feature a teacher needs to run classes and none of the paid extras. It sets no caps.</summary>
    public static TeacherPlan Fallback { get; } =
        new(FreeName, null, new HashSet<string>(), PlanLimits.All.ToDictionary(k => k, _ => (int?)null));

    /// <summary>Adds Free and Pro when there are no plans at all. Plans the Super Admin has changed are never touched.</summary>
    public static async Task EnsureAsync(PlansDbContext db, CancellationToken ct)
    {
        if (await db.Plans.AnyAsync(ct))
        {
            return;
        }

        var free = new Plan { Name = FreeName, Description = "Classes, students, fees, attendance and exams.", IsDefault = true };
        var pro = new Plan { Name = ProName, Description = "Everything in Free, plus WhatsApp notifications, Drive materials and the parent portal." };
        db.Plans.AddRange(free, pro);
        foreach (var key in PlanFeatures.All)
        {
            db.Features.Add(new PlanFeature { PlanId = free.Id, Key = key, Enabled = false });
            db.Features.Add(new PlanFeature { PlanId = pro.Id, Key = key, Enabled = true });
        }

        foreach (var key in PlanLimits.All)
        {
            // Free has no WhatsApp at all, so its allowance is zero rather than unlimited.
            db.Features.Add(new PlanFeature { PlanId = free.Id, Key = key, Enabled = true, LimitValue = key == PlanLimits.WhatsAppPerMonth ? 0 : null });
            db.Features.Add(new PlanFeature { PlanId = pro.Id, Key = key, Enabled = true, LimitValue = key == PlanLimits.WhatsAppPerMonth ? 500 : null });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Today's date in Sri Lanka (UTC+5:30, no daylight saving).</summary>
    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).DateTime);
}
