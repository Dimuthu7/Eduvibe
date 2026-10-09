using EduVibe.Modules.Identity.Domain;
using EduVibe.Shared.Phone;
using EduVibe.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EduVibe.Modules.Identity.Services;

/// <summary>
/// Creates the first Super Admin from SuperAdmin:Phone and SuperAdmin:Password when none exists.
/// The password is a one-time password: the admin must replace it at first sign-in.
/// </summary>
internal sealed class SuperAdminSeeder(
    IServiceScopeFactory scopes, Microsoft.Extensions.Configuration.IConfiguration configuration, ILogger<SuperAdminSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var phone = PhoneNumber.Normalize(configuration["SuperAdmin:Phone"]);
        var password = configuration["SuperAdmin:Password"];
        if (phone is null || string.IsNullOrEmpty(password) || password.Length < PasswordService.MinimumLength)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        if (await db.UserRoles.AnyAsync(r => r.Role == Roles.SuperAdmin, cancellationToken))
        {
            return;
        }

        var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();
        var user = new User { Phone = phone, FirstName = configuration["SuperAdmin:FirstName"] ?? "Super", LastName = configuration["SuperAdmin:LastName"] ?? "Admin", MustChangePassword = true };
        user.PasswordHash = passwords.Hash(user, password);
        user.Roles.Add(new UserRole { Role = Roles.SuperAdmin });
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created the first Super Admin for {Phone}", phone);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
