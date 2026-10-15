using EduVibe.Shared.Modules;
using EduVibe.Shared.Privacy;

namespace EduVibe.Modules.Exams;

/// <summary>Exams rows for a teacher's export and erasure, and for a student's export through their enrolments.</summary>
internal sealed class ExamsPersonalData(ExamsDbContext db, IClassRoster roster) : EnrolmentTablesSource<ExamsDbContext>(db, ExamsModule.ModuleName, roster);
