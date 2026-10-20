using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicHub.Api.Acquisition;

/// <summary>The one way acquisition compares names: lowercase words without diacritics or punctuation.</summary>
public static partial class TextMatch
{
    public static IReadOnlyList<string> Words(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            plain.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>A name as a comparable key: <see cref="Words"/> joined.</summary>
    public static string Key(string text) => string.Join(' ', Words(text));

    /// <summary>A title as a comparable key, ignoring a featured-artist clause ("(feat. X)", "ft. X").</summary>
    public static string TitleKey(string title) => Key(FeaturedClause().Replace(title, ""));

    [GeneratedRegex(@"[\(\[]\s*(feat|ft|featuring)\b[^\)\]]*[\)\]]|\s(feat|ft|featuring)\.?\s.*$", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturedClause();
}
