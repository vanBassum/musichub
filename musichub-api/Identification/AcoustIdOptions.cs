namespace MusicHub.Api.Identification;

/// <summary>Binds the <c>AcoustId</c> configuration section (environment variables <c>AcoustId__*</c>).</summary>
public sealed class AcoustIdOptions
{
    public const string SectionName = "AcoustId";

    /// <summary>The AcoustID application API key (secret). Without it the AcoustID provider is not registered.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The <c>fpcalc</c> (Chromaprint) executable: a name on the PATH or an absolute path.</summary>
    public string FpcalcPath { get; set; } = "fpcalc";

    /// <summary>The AcoustID lookup endpoint.</summary>
    public string Endpoint { get; set; } = "https://api.acoustid.org/v2/lookup";

    /// <summary>How long <c>fpcalc</c> and the AcoustID request may each take.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many seconds of audio <c>fpcalc</c> analyses.</summary>
    public int FingerprintSeconds { get; set; } = 120;

    /// <summary>The largest audio file that is fingerprinted, in megabytes; larger files are refused.</summary>
    public int MaxFileMegabytes { get; set; } = 200;

    /// <summary>How many <c>fpcalc</c> processes may run at once.</summary>
    public int MaxConcurrentFingerprints { get; set; } = 2;

    /// <summary>AcoustID matches scoring below this (0 to 1) are ignored.</summary>
    public double MinScore { get; set; } = 0.8;
}
