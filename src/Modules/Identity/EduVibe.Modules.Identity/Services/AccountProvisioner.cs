using EduVibe.Modules.Identity.Domain;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Identity.Services;

internal sealed class AccountProvisioner(IdentityDbContext db, PasswordService passwords, AuthService auth) : IAccountProvisioner
{
    // Accounts with any of these roles are never reset through a teacher: that would hand a teacher someone else's login.
    private static readonly string[] StaffRoles = [Roles.SuperAdmin, Roles.Teacher, Roles.InstituteAdmin];

    public int MinimumPasswordLength => PasswordService.MinimumLength;

    public string HashPassword(string password) =>
        passwords.Hash(new User { Phone = "", FirstName = "", LastName = "" }, password);

    public async Task<AccountResult> CreateStudentAccountAsync(NewStudentAccount account, bool linkExisting, CancellationToken ct = default)
    {
        var existing = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Phone == account.Phone, ct);
        if (existing is not null)
        {
            if (!linkExisting)
            {
                return new AccountResult(AccountOutcome.PhoneInUse, null, null);
            }

            if (existing.Roles.All(r => r.Role != Roles.Student))
            {
                db.UserRoles.Add(new UserRole { UserId = existing.Id, Role = Roles.Student });
                await db.SaveChangesAsync(ct);
            }

            return new AccountResult(AccountOutcome.Linked, existing.Id, null);
        }

        var user = new User { Phone = account.Phone, FirstName = account.FirstName, LastName = account.LastName };
        string? otp = null;
        if (account.PasswordHash is null)
        {
            otp = PasswordService.NewOneTimePassword();
            user.PasswordHash = passwords.Hash(user, otp);
            user.MustChangePassword = true;
        }
        else
        {
            user.PasswordHash = account.PasswordHash;
        }

        user.Roles.Add(new UserRole { Role = Roles.Student });
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return new AccountResult(AccountOutcome.Created, user.Id, otp);
    }

    public async Task<string?> ResetStudentPasswordAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.Roles.All(r => r.Role != Roles.Student) || user.Roles.Any(r => StaffRoles.Contains(r.Role)))
        {
            return null;
        }

        var otp = PasswordService.NewOneTimePassword();
        user.PasswordHash = passwords.Hash(user, otp);
        user.MustChangePassword = true;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        await auth.RevokeAllAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
        return otp;
    }
}
