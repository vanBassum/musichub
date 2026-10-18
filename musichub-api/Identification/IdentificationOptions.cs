namespace MusicHub.Api.Identification;

public enum IdentificationStrategy
{
    RuleBased,
    Ai,
}

/// <summary>Binds the <c>Identification</c> configuration section (environment variables <c>Identification__*</c>).</summary>
public sealed class IdentificationOptions
{
    public const string SectionName = "Identification";

    /// <summary>Which resolver is used. Defaults to the rule-based one, which needs no AI provider.</summary>
    public IdentificationStrategy Strategy { get; set; } = IdentificationStrategy.RuleBased;

    /// <summary>The AI model to refer to; required when <see cref="Strategy"/> is Ai.</summary>
    public string? AiModel { get; set; }

    /// <summary>How many evidence tools the AI may run for one file before it has to propose or give up.</summary>
    public int AiMaxSteps { get; set; } = 5;
}
