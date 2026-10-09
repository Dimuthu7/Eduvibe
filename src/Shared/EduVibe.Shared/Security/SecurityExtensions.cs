using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace EduVibe.Shared.Security;

public static class SecurityExtensions
{
    public const int MinimumSigningKeyLength = 32;

    /// <summary>JWT sign-in plus the policies every module uses. Endpoints require an active user unless they say otherwise.</summary>
    public static IServiceCollection AddEduVibeSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        if (options.SigningKey.Length < MinimumSigningKeyLength)
        {
            throw new InvalidOperationException(
                $"Jwt:SigningKey must be at least {MinimumSigningKeyLength} characters. Set the Jwt__SigningKey environment variable.");
        }

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    NameClaimType = EduVibeClaims.Subject,
                    RoleClaimType = EduVibeClaims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(ActivePolicy())
            .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !context.User.HasClaim(c => c.Type == EduVibeClaims.MustChangePassword))
                .Build())
            .AddPolicy(Policies.Active, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !context.User.HasClaim(c => c.Type == EduVibeClaims.MustChangePassword)))
            .AddPolicy(Policies.SignedIn, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(Policies.SuperAdmin, policy => policy
                .RequireRole(Roles.SuperAdmin)
                .RequireAssertion(context => !context.User.HasClaim(c => c.Type == EduVibeClaims.MustChangePassword)))
            .AddPolicy(Policies.Teacher, policy => policy
                .RequireRole(Roles.Teacher)
                .RequireClaim(EduVibeClaims.TenantId)
                .RequireAssertion(context => !context.User.HasClaim(c => c.Type == EduVibeClaims.MustChangePassword)));

        return services;
    }

    private static Microsoft.AspNetCore.Authorization.AuthorizationPolicy ActivePolicy() =>
        new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => !context.User.HasClaim(c => c.Type == EduVibeClaims.MustChangePassword))
            .Build();
}
