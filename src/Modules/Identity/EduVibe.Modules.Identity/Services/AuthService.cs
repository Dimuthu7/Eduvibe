using System.Security.Claims;
using System.Text;
using EduVibe.Modules.Identity.Domain;
using EduVibe.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EduVibe.Modules.Identity.Services;

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

/// <summary>Signs users in and issues and rotates tokens.</summary>
public sealed class AuthService(IdentityDbContext db, PasswordService passwords, IOptions<JwtOptions> jwt, TimeProvider clock)
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    private readonly JwtOptions _jwt = jwt.Value;

    public async Task<(TokenPair? Tokens, string? Error)> LoginAsync(string phone, string password, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Phone == phone, ct);

        // The same answer for an unknown phone and a wrong password, so numbers cannot be probed.
        if (user is null || !user.IsActive)
        {
            return (null, "invalid_credentials");
        }

        if (user.LockedUntil > now)
        {
            return (null, "locked");
        }

        if (!passwords.Verify(user, password))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockedUntil = now + LockDuration;
                user.FailedLoginCount = 0;
            }

            await db.SaveChangesAsync(ct);
            return (null, "invalid_credentials");
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        var tokens = await IssueAsync(user, ct);
        return (tokens, null);
    }

    public async Task<TokenPair?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = TokenText.Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null)
        {
            return null;
        }

        if (stored.RevokedAt is not null)
        {
            // A rotated token came back: assume it leaked and end every session of this user.
            await RevokeAllAsync(stored.UserId, ct);
            return null;
        }

        if (stored.ExpiresAt <= now)
        {
            return null;
        }

        var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == stored.UserId, ct);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        stored.RevokedAt = now;
        return await IssueAsync(user, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var hash = TokenText.Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
        if (stored is not null)
        {
            stored.RevokedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    /// <summary>Creates a new token pair and saves everything pending on the context.</summary>
    public async Task<TokenPair> IssueAsync(User user, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(EduVibeClaims.Subject, user.Id.ToString()),
            new("name", user.FullName),
        };
        claims.AddRange(user.Roles.Select(r => new Claim(EduVibeClaims.Role, r.Role)));

        if (user.Roles.Any(r => r.Role == Roles.Teacher))
        {
            var teacherId = await db.Teachers.Where(t => t.UserId == user.Id).Select(t => (Guid?)t.Id).SingleOrDefaultAsync(ct);
            if (teacherId is not null)
            {
                claims.Add(new Claim(EduVibeClaims.TenantId, teacherId.Value.ToString()));
            }
        }

        if (user.MustChangePassword)
        {
            claims.Add(new Claim(EduVibeClaims.MustChangePassword, "true"));
        }

        var access = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)), SecurityAlgorithms.HmacSha256),
        });

        var refresh = TokenText.NewRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenText.Hash(refresh),
            ExpiresAt = now.AddDays(_jwt.RefreshTokenDays),
        });

        await db.SaveChangesAsync(ct);
        return new TokenPair(access, refresh, expires);
    }
}
