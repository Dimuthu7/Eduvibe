using Npgsql;

namespace EduVibe.Shared.Persistence;

/// <summary>Accepts the connection string in either form: Npgsql key=value, or the postgres:// URI that hosts such as Neon hand out.</summary>
public static class PostgresConnectionString
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var text = value.Trim();
        if (!text.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        var uri = new Uri(text);
        var credentials = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port < 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,
            // Hosted databases require TLS; sslmode in the query string can still change it.
            SslMode = SslMode.Require,
        };

        // Only sslmode is carried over; libpq-only options such as channel_binding are not Npgsql keywords.
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase) && parts.Length > 1
                && Enum.TryParse<SslMode>(parts[1], ignoreCase: true, out var mode))
            {
                builder.SslMode = mode;
            }
        }

        return builder.ConnectionString;
    }
}
