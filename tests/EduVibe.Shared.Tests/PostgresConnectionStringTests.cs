using EduVibe.Shared.Persistence;
using Npgsql;

namespace EduVibe.Shared.Tests;

public class PostgresConnectionStringTests
{
    [Fact]
    public void Key_value_strings_pass_through()
    {
        const string value = "Host=localhost;Database=eduvibe;Username=eduvibe;Password=eduvibe";
        Assert.Equal(value, PostgresConnectionString.Normalize(value));
    }

    [Fact]
    public void Uri_from_a_host_becomes_an_npgsql_string_with_tls()
    {
        var text = PostgresConnectionString.Normalize(
            "postgresql://app_owner:p%40ss@ep-cool-123.ap-southeast-1.aws.neon.tech/eduvibe?sslmode=require&channel_binding=require");
        var builder = new NpgsqlConnectionStringBuilder(text);

        Assert.Equal("ep-cool-123.ap-southeast-1.aws.neon.tech", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("eduvibe", builder.Database);
        Assert.Equal("app_owner", builder.Username);
        Assert.Equal("p@ss", builder.Password);
        Assert.Equal(SslMode.Require, builder.SslMode);
    }

    [Fact]
    public void Empty_values_are_left_alone()
    {
        Assert.Null(PostgresConnectionString.Normalize(null));
        Assert.Equal("", PostgresConnectionString.Normalize(""));
    }
}
