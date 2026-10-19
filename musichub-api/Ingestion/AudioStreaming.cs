using System.Net.Http.Headers;

namespace MusicHub.Api.Ingestion;

/// <summary>What a request for stored audio asks for: the whole object, one satisfiable range, or an unsatisfiable range.</summary>
public readonly record struct AudioRange(long Start, long End, bool IsPartial, bool IsSatisfiable)
{
    public long Length => End - Start + 1;
}

public static class AudioStreaming
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".flac"] = "audio/flac",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".opus"] = "audio/ogg",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".weba"] = "audio/webm",
    };

    /// <summary>The audio content type for a filename's extension; null when it isn't a known audio extension.</summary>
    public static string? ContentTypeFor(string filename) =>
        ContentTypes.TryGetValue(Path.GetExtension(filename), out var contentType) ? contentType : null;

    /// <summary>
    /// Resolves a Range header against an object of <paramref name="totalLength"/> bytes. A missing, malformed,
    /// non-byte or multi-range header is ignored (the whole object is served, as RFC 9110 allows); a single
    /// range that starts beyond the end is unsatisfiable. An end beyond the object is clamped to it.
    /// </summary>
    public static AudioRange ResolveRange(string rangeHeader, long totalLength)
    {
        var whole = new AudioRange(0, totalLength - 1, false, true);
        if (!RangeHeaderValue.TryParse(rangeHeader, out var header)
            || !string.Equals(header.Unit, "bytes", StringComparison.OrdinalIgnoreCase)
            || header.Ranges.Count != 1)
            return whole;

        var range = header.Ranges.Single();
        long start, end;
        if (range.From is { } from)
        {
            start = from;
            end = Math.Min(range.To ?? totalLength - 1, totalLength - 1);
            if (start >= totalLength || end < start)
                return new AudioRange(0, 0, true, false);
        }
        else if (range.To is { } suffix and > 0)
        {
            if (totalLength == 0)
                return new AudioRange(0, 0, true, false);
            start = Math.Max(totalLength - suffix, 0);
            end = totalLength - 1;
        }
        else
        {
            return new AudioRange(0, 0, true, false);
        }

        return new AudioRange(start, end, true, true);
    }
}
