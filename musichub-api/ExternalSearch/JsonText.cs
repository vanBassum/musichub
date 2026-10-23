using System.Text.Json;

namespace MusicHub.Api.ExternalSearch;

/// <summary>Reads fields from the JSON an external service returns.</summary>
public static class JsonText
{
    /// <summary>The string value of <paramref name="property"/>, or null when it is missing, not a string, or the element is not an object.</summary>
    public static string? Read(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
