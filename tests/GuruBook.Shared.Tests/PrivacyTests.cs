using GuruBook.Shared.Auditing;
using GuruBook.Shared.Privacy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Shared.Tests;

public class PrivacyTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTenantContext _mine = new() { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };
    private readonly FakeTenantContext _theirs = new() { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };
    private readonly FakeTenantContext _admin = new() { IsSuperAdmin = true };

    public void Dispose() => _db.Dispose();

    private void Add(FakeTenantContext who, string name)
    {
        using var context = _db.CreateContext(who);
        context.Widgets.Add(new Widget { Name = name, Secret = "hunter2" });
        context.SaveChanges();
    }

    [Fact]
    public async Task Reading_a_tenant_gives_only_their_rows_and_leaves_out_secrets()
    {
        Add(_mine, "mine");
        Add(_theirs, "theirs");
        using var admin = _db.CreateContext(_admin);

        var data = await ModuleData.ReadTenantAsync(admin, _mine.TenantId!.Value, CancellationToken.None);

        var rows = Assert.Single(data).Value;
        var row = Assert.IsType<Dictionary<string, object?>>(Assert.Single(rows));
        Assert.Equal("mine", row["Name"]);
        Assert.DoesNotContain("Secret", row.Keys);
    }

    [Fact]
    public async Task Erasing_a_tenant_deletes_their_rows_and_nobody_elses()
    {
        Add(_mine, "mine");
        Add(_mine, "mine too");
        Add(_theirs, "theirs");
        using var admin = _db.CreateContext(_admin);

        await ModuleData.EraseTenantAsync(admin, _mine.TenantId!.Value, CancellationToken.None);
        await ModuleData.EraseTenantAsync(admin, _mine.TenantId!.Value, CancellationToken.None);

        var left = await admin.Widgets.ToListAsync();
        Assert.Equal("theirs", Assert.Single(left).Name);
    }

    [Fact]
    public async Task Redaction_blanks_what_a_teachers_audit_rows_say_but_keeps_the_rows()
    {
        Add(_mine, "Secretive Name");
        Add(_theirs, "Other Name");
        await using var audit = _db.CreateAudit();

        await AuditRedaction.RedactTenantAsync(audit, _mine.TenantId!.Value, CancellationToken.None);
        await AuditRedaction.RedactTenantAsync(audit, _mine.TenantId!.Value, CancellationToken.None);

        var mine = await audit.Entries.Where(e => e.TenantId == _mine.TenantId).ToListAsync();
        var theirs = await audit.Entries.Where(e => e.TenantId == _theirs.TenantId).ToListAsync();
        var entry = Assert.Single(mine);
        Assert.Equal(AuditAction.Created, entry.Action);
        Assert.Equal(AuditRedaction.Erased, entry.After);
        Assert.Null(entry.Before);
        Assert.Contains("Other Name", Assert.Single(theirs).After);
    }

    [Fact]
    public async Task Redacting_records_touches_only_those_records()
    {
        Add(_mine, "First");
        Add(_mine, "Second");
        await using var audit = _db.CreateAudit();
        var rows = await audit.Entries.ToListAsync();
        var first = rows.Single(r => r.After!.Contains("First")).EntityId;

        await AuditRedaction.RedactRecordsAsync(audit, nameof(Widget), [first], CancellationToken.None);

        audit.ChangeTracker.Clear();
        var after = await audit.Entries.ToListAsync();
        Assert.Equal(AuditRedaction.Erased, after.Single(r => r.EntityId == first).After);
        Assert.Contains("Second", after.Single(r => r.EntityId != first).After);
    }
}
