using System.Text.Json.Serialization;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Catalog;

/// <summary>
/// Where a Stored file stands in identification, derived only from what is already recorded: the file's link
/// (<see cref="FileRecord.TrackId"/>), who applied it (<see cref="AppliedIdentification"/>) and how the latest attempt
/// ended (<see cref="IdentificationOutcome"/>). There is no confidence score or threshold: "uncertain" is the existing
/// decision <see cref="IdentificationDecision.InsufficientEvidence"/>, candidates that did not justify an identity.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FileIdentificationFilter
{
    /// <summary>Linked to a track, by the pipeline or by a person.</summary>
    Identified,

    /// <summary>Linked, and the latest applied identity was confirmed or corrected by a person.</summary>
    ManuallyConfirmed,

    /// <summary>Not linked to a track: every state below.</summary>
    Unidentified,

    /// <summary>Not linked, and no unresolved attempt is recorded (not attempted yet, or only skipped).</summary>
    NotAttempted,

    /// <summary>Not linked; the latest attempt found candidates that did not justify an identity.</summary>
    Uncertain,

    /// <summary>Not linked; the latest attempt found nothing.</summary>
    NoMatch,

    /// <summary>Not linked; the latest attempt had no method that could run.</summary>
    NoMethodRan,

    /// <summary>Not linked; the latest attempt found nothing and at least one method failed.</summary>
    MethodFailed,

    /// <summary>Not linked; processing the file failed in the latest attempt.</summary>
    Error,

    /// <summary>Not linked; the latest attempt identified it as another recording than the one wanted.</summary>
    Mismatch,
}

/// <summary>The one owner of how a <see cref="FileIdentificationFilter"/> selects files and how a file's state is named.</summary>
public static class FileIdentification
{
    /// <summary>The unlinked states that name a decision, with the decision each stands for.</summary>
    private static readonly Dictionary<FileIdentificationFilter, IdentificationDecision> DecisionStates = new()
    {
        [FileIdentificationFilter.Uncertain] = IdentificationDecision.InsufficientEvidence,
        [FileIdentificationFilter.NoMatch] = IdentificationDecision.NoMatch,
        [FileIdentificationFilter.NoMethodRan] = IdentificationDecision.NoMethodRan,
        [FileIdentificationFilter.MethodFailed] = IdentificationDecision.MethodFailed,
        [FileIdentificationFilter.Error] = IdentificationDecision.Error,
        [FileIdentificationFilter.Mismatch] = IdentificationDecision.Mismatch,
    };

    /// <summary>
    /// The Stored files in <paramref name="filter"/>. Every Stored file is in exactly one of Identified and Unidentified,
    /// and every Unidentified one in exactly one of NotAttempted and the decision states.
    /// </summary>
    public static IQueryable<FileRecord> Matching(
        this IQueryable<FileRecord> files, MusicHubDbContext db, FileIdentificationFilter filter)
    {
        var stored = files.Where(f => f.Status == FileStatus.Stored);
        var unlinked = stored.Where(f => f.TrackId == null);
        if (DecisionStates.TryGetValue(filter, out var decision))
            return unlinked.Where(f => db.IdentificationOutcomes.Any(o =>
                o.FileId == f.Id && o.Decision == decision
                && !db.IdentificationOutcomes.Any(newer => newer.FileId == o.FileId && newer.Id > o.Id)));
        return filter switch
        {
            FileIdentificationFilter.Identified => stored.Where(f => f.TrackId != null),
            FileIdentificationFilter.ManuallyConfirmed => stored.Where(f => f.TrackId != null
                && db.AppliedIdentifications.Any(a =>
                    a.FileId == f.Id && a.Source == IdentificationSource.Manual
                    && !db.AppliedIdentifications.Any(newer => newer.FileId == a.FileId && newer.Id > a.Id))),
            FileIdentificationFilter.Unidentified => unlinked,
            _ => unlinked.Where(f => !db.IdentificationOutcomes.Any(o =>
                o.FileId == f.Id && IdentificationDecisions.Unresolved.Contains(o.Decision)
                && !db.IdentificationOutcomes.Any(newer => newer.FileId == o.FileId && newer.Id > o.Id))),
        };
    }

    /// <summary>
    /// The most specific state of a Stored file, the same one <see cref="Matching"/> selects it by, given its latest
    /// attempt's decision (null when none) and whether its latest applied identity is manual.
    /// </summary>
    public static FileIdentificationFilter StateOf(
        bool linked, bool manuallyConfirmed, IdentificationDecision? latestDecision)
    {
        if (linked)
            return manuallyConfirmed ? FileIdentificationFilter.ManuallyConfirmed : FileIdentificationFilter.Identified;
        foreach (var (state, decision) in DecisionStates)
            if (decision == latestDecision)
                return state;
        return FileIdentificationFilter.NotAttempted;
    }
}
