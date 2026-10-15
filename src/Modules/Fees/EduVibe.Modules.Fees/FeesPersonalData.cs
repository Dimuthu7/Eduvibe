using EduVibe.Shared.Modules;
using EduVibe.Shared.Privacy;

namespace EduVibe.Modules.Fees;

/// <summary>Fees rows for a teacher's export and erasure, and for a student's export through their enrolments.</summary>
internal sealed class FeesPersonalData(FeesDbContext db, IClassRoster roster) : EnrolmentTablesSource<FeesDbContext>(db, FeesModule.ModuleName, roster);
