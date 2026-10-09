using Microsoft.EntityFrameworkCore;

namespace EduVibe.Shared.Tests;

public class TenancyTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly Guid _teacherA = Guid.NewGuid();
    private readonly Guid _teacherB = Guid.NewGuid();

    public void Dispose() => _db.Dispose();

    private void Seed()
    {
        using var a = _db.CreateContext(new FakeTenantContext { TenantId = _teacherA });
        a.Widgets.Add(new Widget { Name = "A's widget" });
        a.SaveChanges();

        using var b = _db.CreateContext(new FakeTenantContext { TenantId = _teacherB });
        b.Widgets.Add(new Widget { Name = "B's widget" });
        b.SaveChanges();
    }

    [Fact]
    public void A_teacher_only_sees_their_own_rows()
    {
        Seed();

        using var a = _db.CreateContext(new FakeTenantContext { TenantId = _teacherA });

        var names = a.Widgets.Select(w => w.Name).ToList();

        Assert.Equal(["A's widget"], names);
    }

    [Fact]
    public void A_super_admin_sees_every_tenant()
    {
        Seed();

        using var admin = _db.CreateContext(new FakeTenantContext { IsSuperAdmin = true });

        Assert.Equal(2, admin.Widgets.Count());
    }

    [Fact]
    public void A_request_without_a_tenant_sees_nothing()
    {
        Seed();

        using var anonymous = _db.CreateContext(new FakeTenantContext());

        Assert.Empty(anonymous.Widgets);
    }

    [Fact]
    public void Insert_stamps_the_current_tenant()
    {
        using var a = _db.CreateContext(new FakeTenantContext { TenantId = _teacherA });
        var widget = new Widget { Name = "new" };

        a.Widgets.Add(widget);
        a.SaveChanges();

        Assert.Equal(_teacherA, widget.TenantId);
    }

    [Fact]
    public void Insert_without_a_tenant_is_rejected()
    {
        using var anonymous = _db.CreateContext(new FakeTenantContext());
        anonymous.Widgets.Add(new Widget { Name = "orphan" });

        Assert.Throws<InvalidOperationException>(() => anonymous.SaveChanges());
    }

    [Fact]
    public void A_row_cannot_be_moved_to_another_tenant()
    {
        Seed();
        using var a = _db.CreateContext(new FakeTenantContext { TenantId = _teacherA });
        var widget = a.Widgets.Single();

        widget.TenantId = _teacherB;

        Assert.Throws<InvalidOperationException>(() => a.SaveChanges());
    }
}
