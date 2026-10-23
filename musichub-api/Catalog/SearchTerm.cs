using System.Linq.Expressions;

namespace MusicHub.Api.Catalog;

/// <summary>The one search rule of the list queries: trimmed, case-insensitive "contains"; an empty term matches everything.</summary>
public static class SearchTerm
{
    /// <summary>The normalized term, or null when there is nothing to search for.</summary>
    public static string? Normalize(string? search)
    {
        var term = search?.Trim().ToLower();
        return string.IsNullOrEmpty(term) ? null : term;
    }

    /// <summary>Keeps the items whose text matches the term; all of them when the term is empty.</summary>
    public static IQueryable<T> Filter<T>(IQueryable<T> items, Expression<Func<T, string>> text, string? search)
    {
        var term = Normalize(search);
        if (term is null) return items;
        var contains = Expression.Call(
            Expression.Call(text.Body, nameof(string.ToLower), Type.EmptyTypes),
            nameof(string.Contains), null, Expression.Constant(term));
        return items.Where(Expression.Lambda<Func<T, bool>>(contains, text.Parameters));
    }
}
