using GuruBook.Modules.Materials.Drive;
using GuruBook.Shared.Privacy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Materials;

/// <summary>
/// A teacher's connection, class folders and material list are tenant-owned rows, so they are exported (the refresh token is a
/// secret and is left out) and erased with the rest of their data. Erasing also tells Google to drop GuruBook's access. The files
/// themselves are the teacher's own and stay in their Drive; nothing here deletes them.
/// </summary>
internal sealed class MaterialsPersonalData(MaterialsDbContext db, DriveTokens tokens, IDriveClient drive)
    : TenantTablesSource<MaterialsDbContext>(db, MaterialsModule.ModuleName)
{
    public override async Task EraseTeacherAsync(Guid teacherId, CancellationToken ct)
    {
        var connections = await Db.Connections.IgnoreQueryFilters().Where(c => c.TenantId == teacherId).ToListAsync(ct);
        foreach (var connection in connections)
        {
            if (tokens.TryUnprotect(connection.RefreshToken) is { } token)
            {
                await drive.RevokeAsync(token, ct);
            }
        }

        await base.EraseTeacherAsync(teacherId, ct);
    }
}
