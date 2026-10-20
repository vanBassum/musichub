using Hangfire.Server;

namespace MusicHub.Api.Jobs;

/// <summary>
/// How a running job tells the Jobs area how much it set out to do: a Hangfire job parameter, read back with the
/// execution's details. It is Hangfire execution metadata and goes with the execution; nothing else is persisted for it.
/// </summary>
public static class JobProgress
{
    public const string TotalParameter = "Total";

    public static void ReportTotal(PerformContext? context, int total) =>
        context?.Connection.SetJobParameter(context.BackgroundJob.Id, TotalParameter, total.ToString());
}
