using EduVibe.Shared.Modules;
using EduVibe.Shared.Privacy;

namespace EduVibe.Modules.Attendance;

/// <summary>Attendance rows for a teacher's export and erasure, and for a student's export through their enrolments.</summary>
internal sealed class AttendancePersonalData(AttendanceDbContext db, IClassRoster roster) : EnrolmentTablesSource<AttendanceDbContext>(db, AttendanceModule.ModuleName, roster);
