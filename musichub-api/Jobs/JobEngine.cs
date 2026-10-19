using Hangfire;

namespace MusicHub.Api.Jobs;

/// <summary>
/// The running Hangfire storage and client. <see cref="HangfireServerService"/> publishes them once the schema is
/// ready; until then the accessors wait, so a caller never meets a half-started engine.
/// </summary>
public sealed class JobEngine
{
    private readonly TaskCompletionSource<(JobStorage Storage, IBackgroundJobClient Client)> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsReady => _ready.Task.IsCompletedSuccessfully;

    internal void Publish(JobStorage storage, IBackgroundJobClient client) => _ready.TrySetResult((storage, client));

    public async Task<IBackgroundJobClient> ClientAsync(CancellationToken cancellationToken) =>
        (await _ready.Task.WaitAsync(cancellationToken)).Client;

    public async Task<JobStorage> StorageAsync(CancellationToken cancellationToken) =>
        (await _ready.Task.WaitAsync(cancellationToken)).Storage;
}
