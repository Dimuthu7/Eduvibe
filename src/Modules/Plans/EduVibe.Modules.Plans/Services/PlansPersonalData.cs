using EduVibe.Modules.Plans.Domain;
using EduVibe.Shared.Privacy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Plans.Services;

/// <summary>A teacher's plan history and the payments recorded for them. These rows hold the teacher's id, not a tenant id.</summary>
internal sealed class PlansPersonalData(PlansDbContext db) : IPersonalDataSource
{
    public string Module => PlansModule.ModuleName;

    public async Task<PersonalData> ExportTeacherAsync(Guid teacherId, CancellationToken ct)
    {
        var data = new PersonalData();
        var subscriptions = await db.Subscriptions.AsNoTracking().Where(s => s.TeacherId == teacherId).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Where(p => p.TeacherId == teacherId).ToListAsync(ct);
        if (subscriptions.Count > 0)
        {
            data["teacher_subscriptions"] = [.. subscriptions.Select(s => (object)ModuleData.Project(db.Model.FindEntityType(typeof(TeacherSubscription))!, s))];
        }

        if (payments.Count > 0)
        {
            data["plan_payments"] = [.. payments.Select(p => (object)ModuleData.Project(db.Model.FindEntityType(typeof(PlanPayment))!, p))];
        }

        return data;
    }

    public async Task EraseTeacherAsync(Guid teacherId, CancellationToken ct)
    {
        await db.Payments.Where(p => p.TeacherId == teacherId).ExecuteDeleteAsync(ct);
        await db.Subscriptions.Where(s => s.TeacherId == teacherId).ExecuteDeleteAsync(ct);
    }
}
