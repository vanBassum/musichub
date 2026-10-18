namespace MusicHub.Api.Identification;

/// <summary>
/// An external catalog search by artist and title. No implementation is registered by default: register one to
/// enable <see cref="CandidateSearchEvidenceProvider"/>.
/// </summary>
public interface ICandidateSearch
{
    /// <summary>The service's name, e.g. its catalog; carried on the evidence.</summary>
    string Source { get; }

    Task<IReadOnlyList<EvidenceCandidate>> SearchAsync(
        string title, IReadOnlyList<string> artists, CancellationToken ct);
}

/// <summary>
/// Searches for candidates with the artist and title the tags name, as hints. Reports itself unavailable when no
/// <see cref="ICandidateSearch"/> is registered.
/// </summary>
public sealed class CandidateSearchEvidenceProvider(ICandidateSearch? search = null) : IIdentificationEvidenceProvider
{
    public string Source => search?.Source ?? "candidate-search";

    public async Task<EvidenceReport> CollectAsync(IdentificationContext context, CancellationToken ct)
    {
        if (search is null)
        {
            return EvidenceReport.Unavailable(Source, "No candidate search service is configured.");
        }

        if (!context.TryGetTagHint(out var title, out var artists))
        {
            return EvidenceReport.NothingFound(Source, "No title and artist hints to search for.");
        }

        var query = $"\"{title}\" by {string.Join(", ", artists)}";
        var candidates = await search.SearchAsync(title, artists, ct);
        return candidates.Count == 0
            ? EvidenceReport.NothingFound(Source, $"No match for {query}.")
            : EvidenceReport.Provided(Source, candidates, $"Search for {query}.");
    }
}
