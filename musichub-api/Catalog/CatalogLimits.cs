namespace MusicHub.Api.Catalog;

/// <summary>The column length limits of the catalog tables, declared once for the schema and for input validation.</summary>
public static class CatalogLimits
{
    public const int Provider = 100;
    public const int ExternalId = 200;
    public const int Title = 500;
    public const int Version = 500;
    public const int ArtistName = 500;
    public const int Role = 100;
}
