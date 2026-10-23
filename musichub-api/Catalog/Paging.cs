namespace MusicHub.Api.Catalog;

/// <summary>Page/pageSize rules shared by the paged list endpoints.</summary>
public static class Paging
{
    /// <summary>The rows of the 1-based page; the offset is capped so a huge page number cannot overflow.</summary>
    public static IQueryable<T> Page<T>(this IQueryable<T> query, int page, int pageSize) => query
        .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize);

    /// <summary>Adds an error to <paramref name="errors"/> for each of page and pageSize that is out of range.</summary>
    public static void Validate(IDictionary<string, string[]> errors, int page, int pageSize, int maxPageSize)
    {
        if (page < 1) errors["page"] = ["Page must be 1 or greater."];
        if (pageSize < 1 || pageSize > maxPageSize)
            errors["pageSize"] = [$"Page size must be between 1 and {maxPageSize}."];
    }
}
