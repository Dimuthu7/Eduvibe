using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Students;

internal sealed class StudentCards(StudentsDbContext db) : IStudentCards
{
    public async Task<Guid?> StudentOfAsync(string code, CancellationToken cancellationToken = default) =>
        await db.StudentCards.Where(c => c.Code == code).Select(c => (Guid?)c.StudentId).FirstOrDefaultAsync(cancellationToken);
}
