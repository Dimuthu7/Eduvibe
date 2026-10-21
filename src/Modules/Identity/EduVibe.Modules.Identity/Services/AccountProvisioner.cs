using EduVibe.Modules.Identity.Domain;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Identity.Services;

internal sealed class AccountProvisioner(IdentityDbContext db, PasswordService passwords, AuthService auth, IdentityPersonalData erasure) : IAccountProvisioner
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

    public async Task<AccountResult> CreateParentAccountAsync(NewParentAccount account, CancellationToken ct = default)
    {
        var existing = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Phone == account.Phone, ct);
        if (existing is not null)
        {
            if (existing.Roles.All(r => r.Role != Roles.Parent))
            {
                db.UserRoles.Add(new UserRole { UserId = existing.Id, Role = Roles.Parent });
                await db.SaveChangesAsync(ct);
            }

            return new AccountResult(AccountOutcome.Linked, existing.Id, null);
        }

        var user = new User { Phone = account.Phone, FirstName = account.FirstName, LastName = account.LastName };
        var otp = PasswordService.NewOneTimePassword();
        user.PasswordHash = passwords.Hash(user, otp);
        user.MustChangePassword = true;
        user.Roles.Add(new UserRole { Role = Roles.Parent });
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return new AccountResult(AccountOutcome.Created, user.Id, otp);
    }

    public Task<string?> ResetParentPasswordAsync(Guid userId, CancellationToken ct = default) => ResetAsync(userId, Roles.Parent, ct);

    public Task RemoveParentAccountAsync(Guid userId, CancellationToken ct = default) => RemoveRoleAsync(userId, Roles.Parent, ct);

    public Task<string?> ResetStudentPasswordAsync(Guid userId, CancellationToken ct = default) => ResetAsync(userId, Roles.Student, ct);

    // Only an account whose single role is the one the caller manages is reset: anything more would hand a teacher someone else's login.
    private async Task<string?> ResetAsync(Guid userId, string role, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.Roles.All(r => r.Role != role) || user.Roles.Any(r => StaffRoles.Contains(r.Role) || (role == Roles.Parent && r.Role != Roles.Parent)))
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

    public Task RemoveStudentAccountAsync(Guid userId, CancellationToken ct = default) => RemoveRoleAsync(userId, Roles.Student, ct);

    private async Task RemoveRoleAsync(Guid userId, string role, CancellationToken ct)
    {
        await db.UserRoles.Where(r => r.UserId == userId && r.Role == role).ExecuteDeleteAsync(ct);
        if (!await db.UserRoles.AnyAsync(r => r.UserId == userId, ct) && await db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            await erasure.DeleteUserAsync(userId, ct);
        }
    }
}
