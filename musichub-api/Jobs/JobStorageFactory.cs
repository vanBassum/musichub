using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Microsoft.Extensions.Options;
using MusicHub.Api.Data;

namespace MusicHub.Api.Jobs;

/// <summary>Creates the Hangfire storage. Creating it may reach the database, so it can throw while the database is down.</summary>
public interface IJobStorageFactory
{
    JobStorage Create();
}

/// <summary>PostgreSQL storage in the existing database (<c>Database__*</c>), in its own schema. The schema is created or upgraded here.</summary>
public sealed class PostgreSqlJobStorageFactory(IOptions<DatabaseOptions> database, IOptions<HangfireOptions> hangfire) : IJobStorageFactory
{
    public JobStorage Create()
    {
        var storageOptions = new PostgreSqlStorageOptions
        {
            SchemaName = hangfire.Value.SchemaName,
            PrepareSchemaIfNecessary = true,
        };
        return new PostgreSqlStorage(new NpgsqlConnectionFactory(database.Value.BuildConnectionString(), storageOptions), storageOptions);
    }
}
