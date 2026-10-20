using System.Text.Json;
using System.Text.Json.Serialization;
using MusicHub.Api.Identification;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Catalog;

/// <summary>How an attempt to identify one file in a reconciliation run ended. Distinguishes the ways a file stays unlinked.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IdentificationDecision
{
    /// <summary>An identity was established and the file was linked to a track.</summary>
    Identified,

    /// <summary>The tools that ran found nothing.</summary>
    NoMatch,

    /// <summary>Candidates exist but did not justify an identity (they conflict, or only the embedded tags name one).</summary>
    InsufficientEvidence,

    /// <summary>No tool could run (none is configured, or the file is not readable audio): the file was not identified at all.</summary>
    NoMethodRan,

    /// <summary>No candidate, and at least one tool failed, so a match may have been missed.</summary>
    MethodFailed,

    /// <summary>The file was no longer eligible when its turn came; it was not touched.</summary>
    Skipped,

    /// <summary>Processing the file failed (storage, identification or database); it stays unlinked.</summary>
    Error,
}

/// <summary>
/// The one consolidated diagnostic of one file's identification in one Hangfire execution: the decision and its reason,
/// and what each method did (<see cref="MethodsJson"/>, a serialized list of <see cref="Identification.IdentificationMethodResult"/>).
/// It records what happened; it never decides anything. Rows are kept as long as Hangfire keeps the execution.
/// </summary>
public class IdentificationOutcome
{
    /// <summary>Column length of <see cref="FileName"/>; the mapping and <see cref="Truncate"/> both use it.</summary>
    public const int FileNameMaxLength = 500;

    /// <summary>Column length of <see cref="Reason"/>; the mapping and <see cref="Truncate"/> both use it.</summary>
    public const int ReasonMaxLength = 2000;

    /// <summary>Cuts a value to a column length so the row can always be stored.</summary>
    public static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] : value;

    public int Id { get; set; }

    /// <summary>The Hangfire job id of the execution.</summary>
    public string JobExecutionId { get; set; } = "";

    public int FileId { get; set; }
    public FileRecord? File { get; set; }

    /// <summary>The file's name when it was examined.</summary>
    public string FileName { get; set; } = "";

    public IdentificationDecision Decision { get; set; }
    public string Reason { get; set; } = "";

    /// <summary>The track the file was linked to, when <see cref="Decision"/> is Identified.</summary>
    public int? TrackId { get; set; }

    /// <summary>The serialized method results; only <see cref="SetMethods"/> and <see cref="ReadMethods"/> know the format.</summary>
    public string MethodsJson { get; set; } = "[]";
    public DateTimeOffset RecordedAt { get; set; }

    private static readonly JsonSerializerOptions MethodsJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Stores what each method did in <see cref="MethodsJson"/>.</summary>
    public void SetMethods(IReadOnlyList<IdentificationMethodResult> methods) =>
        MethodsJson = JsonSerializer.Serialize(methods, MethodsJsonOptions);

    /// <summary>What each method did; an unreadable <see cref="MethodsJson"/> gives an empty list, so one bad row never hides the rest.</summary>
    public IReadOnlyList<IdentificationMethodResult> ReadMethods()
    {
        try
        {
            return JsonSerializer.Deserialize<List<IdentificationMethodResult>>(MethodsJson, MethodsJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
