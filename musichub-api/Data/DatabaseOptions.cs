using Npgsql;

namespace MusicHub.Api.Data;

/// <summary>PostgreSQL settings, bound to the <c>Database</c> section (<c>Database__*</c> environment keys).</summary>
public class DatabaseOptions
{
    public const string SectionName = "Database";

    public string? Host { get; set; }
    public int Port { get; set; } = 5432;
    public string? Database { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    public string BuildConnectionString() => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Database = Database,
        Username = Username,
        Password = Password,
        Timeout = 5,
    }.ConnectionString;
}
