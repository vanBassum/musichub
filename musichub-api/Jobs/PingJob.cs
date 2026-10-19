namespace MusicHub.Api.Jobs;

/// <summary>A harmless job that proves the engine runs jobs: it logs and returns. Nothing in it comes from a caller.</summary>
public class PingJob(ILogger<PingJob> logger)
{
    public void Run()
    {
        logger.LogInformation("Ping job ran.");
    }
}
