namespace MusicHub.Api.Jobs;

/// <summary>Hangfire settings, bound to the <c>Hangfire</c> section (<c>Hangfire__*</c> environment keys).</summary>
public class HangfireOptions
{
    public const string SectionName = "Hangfire";

    /// <summary>Concurrent job executions in this process.</summary>
    public int WorkerCount { get; set; } = 2;

    /// <summary>How often a failed job is retried before it stays Failed (0 = never retried).</summary>
    public int RetryAttempts { get; set; } = 3;

    /// <summary>How long finished jobs and their history are kept.</summary>
    public int RetentionHours { get; set; } = 168;

    /// <summary>How long a stopping process waits for running jobs; the jobs it abandons are picked up again after the restart.</summary>
    public int ShutdownTimeoutSeconds { get; set; } = 15;

    /// <summary>The PostgreSQL schema Hangfire keeps its tables in, next to MusicHub's own tables.</summary>
    public string SchemaName { get; set; } = "hangfire";

    /// <summary>Why the settings cannot be used, or null when they are valid.</summary>
    public string? Validate() =>
        WorkerCount is < 1 or > 32 ? "Hangfire:WorkerCount must be between 1 and 32."
        : RetryAttempts is < 0 or > 20 ? "Hangfire:RetryAttempts must be between 0 and 20."
        : RetentionHours < 1 ? "Hangfire:RetentionHours must be at least 1."
        : ShutdownTimeoutSeconds is < 1 or > 25 ? "Hangfire:ShutdownTimeoutSeconds must be between 1 and 25 (the host stops after 30)."
        : string.IsNullOrWhiteSpace(SchemaName) ? "Hangfire:SchemaName must not be empty."
        : null;
}
