using System.Text.Json;
using System.Text.RegularExpressions;
using MusicHub.Api.ExternalSearch;

namespace MusicHub.Api.Identification;

// The user-facing search shares the instance, and so the rate-limit gate, retries and failure handling, with the
// identification candidate search: MusicBrainz sees one client, not two.
public sealed partial class MusicBrainzCandidateSearch : IExternalMusicSearch
{
    public string Provider => Source;

    public async Task<IReadOnlyList<ExternalTrack>> SearchTracksAsync(
        string term, ExternalSearchFields fields, int limit, int offset, CancellationToken ct)
    {
        var phrase = Quote(term);
        var clauses = new List<string>();
        if (fields.Title)
        {
            clauses.Add($"recording:\"{phrase}\"");
        }
        if (fields.Artist)
        {
            clauses.Add($"artist:\"{phrase}\"");
        }
        if (clauses.Count == 0)
        {
            return [];
        }
        // "Artist - Title" or "Artist Title" is no phrase of either field: every word has to be found in one of them.
        if (fields is { Title: true, Artist: true } && Words(term) is { Count: > 1 } words)
        {
            clauses.Add("(" + string.Join(" AND ", words.Select(w => $"(recording:\"{w}\" OR artist:\"{w}\")")) + ")");
        }

        var query = string.Join(" OR ", clauses);
        using var json = await GetJsonAsync(
            $"recording?query={Uri.EscapeDataString(query)}&limit={limit}&offset={offset}&fmt=json", ct);
        return Parse(json, "recordings", ToExternalTrack);
    }

    // The words of a term: runs of letters and digits, so separators such as " - " never become a word and nothing
    // that is special to the query syntax can reach it.
    private static List<string> Words(string term) =>
        WordPattern().Matches(term).Select(m => m.Value).ToList();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();

    public async Task<IReadOnlyList<ExternalArtist>> SearchArtistsAsync(string term, int limit, int offset, CancellationToken ct)
    {
        var query = $"artist:\"{Quote(term)}\"";
        using var json = await GetJsonAsync(
            $"artist?query={Uri.EscapeDataString(query)}&limit={limit}&offset={offset}&fmt=json", ct);
        return Parse(json, "artists", ToExternalArtist);
    }

    // The identifier is validated by the caller (a UUID), so it is safe in the path.
    public async Task<ExternalArtist?> GetArtistAsync(string externalId, CancellationToken ct)
    {
        using var json = await GetJsonOrNullAsync($"artist/{Uri.EscapeDataString(externalId)}?fmt=json", ct);
        if (json is null)
        {
            return null;
        }
        try
        {
            return ToExternalArtist(json.RootElement);
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new CandidateSearchUnavailableException("MusicBrainz answered in an unexpected shape.", e);
        }
    }

    public async Task<IReadOnlyList<ExternalTrack>> GetArtistTracksAsync(
        string externalId, int limit, CancellationToken ct)
    {
        using var json = await GetJsonAsync(
            $"recording?artist={Uri.EscapeDataString(externalId)}&inc=artist-credits&limit={limit}&fmt=json", ct);
        return Parse(json, "recordings", ToExternalTrack);
    }

    private static List<T> Parse<T>(JsonDocument json, string property, Func<JsonElement, T?> convert)
        where T : class
    {
        var items = new List<T>();
        try
        {
            if (json.RootElement.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in array.EnumerateArray())
                {
                    if (convert(element) is { } item)
                    {
                        items.Add(item);
                    }
                }
            }
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new CandidateSearchUnavailableException("MusicBrainz answered in an unexpected shape.", e);
        }
        return items;
    }

    private ExternalTrack? ToExternalTrack(JsonElement recording)
    {
        var id = Text(recording, "id");
        var title = Text(recording, "title");
        // No id or title: nothing to name or to refer to later, so nothing to offer.
        if (id is null || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new ExternalTrack(
            Provider, id, title, NonEmpty(Text(recording, "disambiguation")), $"{SiteUrl}recording/{id}", Credits(recording));
    }

    // The credited names as the entity prints them (a credit may rename the artist), with the artist's id when given.
    private static List<ExternalArtistCredit> Credits(JsonElement entity)
    {
        var artists = new List<ExternalArtistCredit>();
        if (entity.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array)
        {
            foreach (var credit in credits.EnumerateArray())
            {
                var artist = credit.TryGetProperty("artist", out var a) ? a : default;
                var name = Text(credit, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = Text(artist, "name");
                }
                if (!string.IsNullOrWhiteSpace(name))
                {
                    artists.Add(new ExternalArtistCredit(name, Text(artist, "id")));
                }
            }
        }
        return artists;
    }

    private ExternalArtist? ToExternalArtist(JsonElement artist)
    {
        var id = Text(artist, "id");
        var name = Text(artist, "name");
        return id is null || string.IsNullOrWhiteSpace(name)
            ? null
            : new ExternalArtist(
                Provider, id, name, NonEmpty(Text(artist, "disambiguation")), $"{SiteUrl}artist/{id}",
                NonEmpty(Text(artist, "type")),
                artist.TryGetProperty("area", out var area) ? NonEmpty(Text(area, "name")) : null);
    }

    private static string? NonEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
