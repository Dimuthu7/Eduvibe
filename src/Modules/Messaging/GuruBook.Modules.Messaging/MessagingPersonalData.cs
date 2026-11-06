using GuruBook.Shared.Privacy;

namespace GuruBook.Modules.Messaging;

/// <summary>
/// A teacher's message settings and their own wording are tenant-owned rows and hold nothing about a student, so they go with the rest of the
/// teacher's data: exported on request, erased with the teacher.
/// </summary>
internal sealed class MessagingPersonalData(MessagingDbContext db)
    : TenantTablesSource<MessagingDbContext>(db, MessagingModule.ModuleName), IPersonalDataSource;
