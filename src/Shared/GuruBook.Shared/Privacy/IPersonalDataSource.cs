namespace GuruBook.Shared.Privacy;

/// <summary>What one module holds about a person, as tables of rows (table name to rows) ready to be written as JSON.</summary>
public sealed class PersonalData : Dictionary<string, IReadOnlyList<object>>
{
    public static readonly PersonalData Empty = [];
}

/// <summary>
/// A module's part in answering a request for someone's data: handing it over and erasing it. Each module that holds personal data
/// implements the half that applies, and the Privacy module gathers them, so a new module takes part by registering one.
/// The Super Admin makes these requests; they act across teachers, so a source must not apply the signed-in teacher's filter.
/// </summary>
public interface IPersonalDataSource
{
    /// <summary>The module's name, the section of the export it fills.</summary>
    string Module { get; }

    /// <summary>Teacher erasure runs sources in this order, lowest first. The account itself goes last.</summary>
    int Order => 0;

    /// <summary>Everything the module holds for one teacher: their rows, and the students' details that only they can see.</summary>
    Task<PersonalData> ExportTeacherAsync(Guid teacherId, CancellationToken cancellationToken) => Task.FromResult(PersonalData.Empty);

    /// <summary>Deletes what the module holds for one teacher. Safe to run again after a failure part way.</summary>
    Task EraseTeacherAsync(Guid teacherId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Everything the module holds about one student, across all teachers.</summary>
    Task<PersonalData> ExportStudentAsync(Guid studentId, CancellationToken cancellationToken) => Task.FromResult(PersonalData.Empty);

    /// <summary>Removes who the student is. Teachers keep their records of fees, attendance and marks, which no longer say whose they are.</summary>
    Task EraseStudentAsync(Guid studentId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The student's full name when this module knows the student, so the Super Admin can confirm the right person.</summary>
    Task<string?> FindStudentNameAsync(Guid studentId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
