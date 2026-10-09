namespace EduVibe.Shared.Security;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "eduvibe";
    public string Audience { get; set; } = "eduvibe";

    /// <summary>At least 32 characters. Set it through the environment in production, never in a file.</summary>
    public string SigningKey { get; set; } = "";

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}
