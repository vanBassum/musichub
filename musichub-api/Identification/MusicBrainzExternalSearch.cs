using System.Text.Json;
using MusicHub.Api.ExternalSearch;

namespace MusicHub.Api.Identification;

// The user-facing search shares the instance, and so the rate-limit gate, retries and failure handling, with the
// identification candidate search: MusicBrainz sees one client, not two.
public sealed partial class MusicBrainzCandidateSearch : IExternalMusicSearch
{
    public string Provider => Source;

    public async Task<IReadOnlyList<ExternalTrack>> SearchTracksAsync(
        string term, ExternalSearchFields fields, int limit, CancellationToken ct)
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

        var query = string.Join(" OR ", clauses);
        using var json = await GetJsonAsync($"recording?query={Uri.EscapeDataString(query)}&limit={limit}&fmt=json", ct);
        return Parse(json, "recordings", ToExternalTrack);
    }

    public async Task<IReadOnlyList<ExternalArtist>> SearchArtistsAsync(string term, int limit, CancellationToken ct)
    {
        var query = $"artist:\"{Quote(term)}\"";
        using var json = await GetJsonAsync($"artist?query={Uri.EscapeDataString(query)}&limit={limit}&fmt=json", ct);
        return Parse(json, "artists", ToExternalArtist);
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

        var artists = new List<ExternalArtistCredit>();
        if (recording.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array)
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
        return new ExternalTrack(Provider, id, title, NonEmpty(Text(recording, "disambiguation")), $"{SiteUrl}recording/{id}", artists);
    }

    private ExternalArtist? ToExternalArtist(JsonElement artist)
    {
        var id = Text(artist, "id");
        var name = Text(artist, "name");
        return id is null || string.IsNullOrWhiteSpace(name)
            ? null
            : new ExternalArtist(Provider, id, name, NonEmpty(Text(artist, "disambiguation")), $"{SiteUrl}artist/{id}");
    }

    private static string? NonEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
