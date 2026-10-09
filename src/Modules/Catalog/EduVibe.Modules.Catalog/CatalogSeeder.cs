using EduVibe.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EduVibe.Modules.Catalog;

/// <summary>
/// Fills empty streams and subjects with a starting list for Sri Lankan tuition. It runs only while a
/// table is empty, so lists the Super Admin has changed are never touched. Turn off with Catalog:SeedOnStartup=false.
/// </summary>
internal sealed class CatalogSeeder(IServiceScopeFactory scopes, IConfiguration configuration) : IHostedService
{
    private static readonly string[] Streams = ["Below Grade 5", "Grade 6 to 9", "O/L", "A/L", "Courses"];

    private static readonly string[] Subjects =
    [
        "Mathematics", "Science", "English", "Sinhala", "Tamil", "History", "Geography", "Buddhism", "Catholicism",
        "Christianity", "Islam", "Hinduism", "ICT", "Commerce", "Business and Accounting Studies", "Health Science", "Art", "Music", "Dancing",
        "Combined Mathematics", "Physics", "Chemistry", "Biology", "Accounting", "Economics", "Business Studies",
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Catalog:SeedOnStartup", true))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        if (!await db.Streams.AnyAsync(cancellationToken))
        {
            db.Streams.AddRange(Streams.Select((name, i) => new EducationStream { Name = name, SortOrder = (i + 1) * 10 }));
        }

        if (!await db.Subjects.AnyAsync(cancellationToken))
        {
            db.Subjects.AddRange(Subjects.Select((name, i) => new Subject { Name = name, SortOrder = (i + 1) * 10 }));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
