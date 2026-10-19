using Npgsql;
using MusicHub.Api.Data;

namespace MusicHub.Api.Tests.Jobs;

/// <summary>
/// A test that needs a real PostgreSQL: it runs when <c>MUSICHUB_TEST_POSTGRES</c> holds a connection string of a
/// disposable database (the test creates and drops its own schema), and is reported as skipped otherwise.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string Variable = "MUSICHUB_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            Skip = $"Set {Variable} to a PostgreSQL connection string to run the persistence tests.";
    }

    public static string? ConnectionString => Environment.GetEnvironmentVariable(Variable);

    public static DatabaseOptions Database()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
        return new DatabaseOptions
        {
            Host = builder.Host,
            Port = builder.Port,
            Database = builder.Database,
            Username = builder.Username,
            Password = builder.Password,
        };
    }
}
