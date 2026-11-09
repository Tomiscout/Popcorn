namespace Popcorn.Domain.Models;

/// <summary>
/// The media a library item was added from: a media source plugin's details page (its id) and the IMDb id it gave.
/// Lets image and description lookups ask for exactly that title instead of searching by name.
/// </summary>
public record MediaSourceRef
{
    /// <summary>Media source plugin id (<c>plugin.json</c> id).</summary>
    public string? PluginId { get; init; }
    /// <summary>That plugin's own id of the media (<c>MediaDetails.Id</c>, e.g. "tv:246" for TMDb).</summary>
    public string? MediaId { get; init; }
    /// <summary>"tt1234567".</summary>
    public string? ImdbId { get; init; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(ImdbId) && (string.IsNullOrWhiteSpace(PluginId) || string.IsNullOrWhiteSpace(MediaId));

    /// <summary>Trimmed copy, null when nothing usable is left.</summary>
    public static MediaSourceRef? Clean(MediaSourceRef? source)
    {
        if (source is null) return null;
        static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var cleaned = new MediaSourceRef { PluginId = Trim(source.PluginId), MediaId = Trim(source.MediaId), ImdbId = Trim(source.ImdbId) };
        return cleaned.IsEmpty ? null : cleaned;
    }
}
