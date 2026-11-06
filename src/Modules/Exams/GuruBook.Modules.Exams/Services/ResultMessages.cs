using System.Globalization;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Tenancy;

namespace GuruBook.Modules.Exams.Services;

/// <summary>
/// The results message for each student in a published exam: their marks, grade and rank. Absent students and students whose marks are still
/// missing are left out, and a student is sent an exam's results once, whether they go out on their own when the exam is published or because
/// the teacher pressed Send.
/// </summary>
internal sealed class ResultMessages(ExamReader reader, IClassRoster roster, IStudentContacts contacts, ITeacherDirectory teachers, ITenantContext user)
{
    public sealed record Plan(List<MessageDraft> Drafts, Dictionary<Guid, string> Names);

    public async Task<(Plan? Plan, string? Error)> BuildAsync(Guid examId, CancellationToken ct)
    {
        var exam = await reader.DetailAsync(examId, ct);
        if (exam is null)
        {
            return (null, "exam_not_found");
        }

        if (exam.PublishedAt is null)
        {
            return (null, "exam_not_published");
        }

        var results = exam.Students.Where(s => s.IsActive && !s.Absent && s.Total is not null).ToList();
        var ranked = exam.Students.Count(s => s.Rank is not null);
        var people = await roster.FindEnrolmentsAsync(results.Select(r => r.EnrolmentId), ct);
        var found = await contacts.FindAsync(people.Values.Select(p => p.StudentId), ct);
        var teacher = user.TenantId is { } tenantId ? await teachers.FindNameAsync(tenantId, ct) ?? "" : "";

        var drafts = new List<MessageDraft>();
        var names = new Dictionary<Guid, string>();
        foreach (var result in results.OrderBy(r => r.IndexNumber))
        {
            if (!people.TryGetValue(result.EnrolmentId, out var person))
            {
                continue;
            }

            names[person.StudentId] = person.FullName;
            var first = found.TryGetValue(person.StudentId, out var contact) ? contact.FirstName : person.FullName;
            drafts.Add(new MessageDraft(
                person.StudentId,
                [
                    first, exam.Name, exam.ClassTitle, $"{Mark(result.Total!.Value)} / {Mark(exam.MaxTotal)}", result.Grade ?? "-",
                    result.Rank is { } rank ? $"{rank} of {ranked}" : "-", teacher,
                ],
                $"result:{examId}:{person.StudentId}"));
        }

        return (new Plan(drafts, names), null);
    }

    private static string Mark(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
