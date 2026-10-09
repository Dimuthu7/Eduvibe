using EduVibe.Shared.Auditing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Shared.Tests;

public class AuditingTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTenantContext _tenant = new() { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };

    public void Dispose() => _db.Dispose();

    private List<AuditLogEntry> Entries()
    {
        using var admin = _db.CreateContext(new FakeTenantContext { IsSuperAdmin = true });
        return admin.Set<AuditLogEntry>().ToList().OrderBy(e => e.At).ToList();
    }

    [Fact]
    public void Create_update_and_delete_are_each_logged()
    {
        using var context = _db.CreateContext(_tenant);
        var widget = new Widget { Name = "first" };
        context.Widgets.Add(widget);
        context.SaveChanges();

        widget.Name = "second";
        context.SaveChanges();

        context.Widgets.Remove(widget);
        context.SaveChanges();

        var entries = Entries();
        Assert.Equal([AuditAction.Created, AuditAction.Updated, AuditAction.Deleted], entries.Select(e => e.Action));
        Assert.All(entries, e =>
        {
            Assert.Equal(nameof(Widget), e.Entity);
            Assert.Equal(widget.Id, e.EntityId);
            Assert.Equal(_tenant.TenantId, e.TenantId);
            Assert.Equal(_tenant.UserId, e.UserId);
        });
    }

    [Fact]
    public void An_update_records_only_the_changed_fields()
    {
        using var context = _db.CreateContext(_tenant);
        var widget = new Widget { Name = "before" };
        context.Widgets.Add(widget);
        context.SaveChanges();

        widget.Name = "after";
        context.SaveChanges();

        var update = Entries().Single(e => e.Action == AuditAction.Updated);
        Assert.Contains("before", update.Before);
        Assert.Contains("after", update.After);
        Assert.DoesNotContain("TenantId", update.After);
    }

    [Fact]
    public void Timestamps_are_set_on_create_and_refreshed_on_update()
    {
        using var context = _db.CreateContext(_tenant);
        var widget = new Widget { Name = "x" };
        context.Widgets.Add(widget);
        context.SaveChanges();
        var created = widget.CreatedAt;
        Assert.NotEqual(default, created);

        widget.Name = "y";
        context.SaveChanges();

        Assert.Equal(created, widget.CreatedAt);
        Assert.True(widget.UpdatedAt >= created);
    }

    [Fact]
    public void Not_audited_fields_never_reach_the_log_and_alone_cause_no_row()
    {
        using var context = _db.CreateContext(_tenant);
        var widget = new Widget { Name = "w", Secret = "hunter2" };
        context.Widgets.Add(widget);
        context.SaveChanges();

        widget.Secret = "hunter3";
        context.SaveChanges();

        var entries = Entries();
        Assert.Single(entries);
        Assert.DoesNotContain("hunter", entries[0].After);
    }
}
