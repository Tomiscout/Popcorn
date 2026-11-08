namespace Popcorn.Shared.Utils;

/// <summary>
/// A .torrent file carried in its link: <c>data:application/x-bittorrent;base64,…</c>. File source plugins return these
/// for .torrent files only they can fetch (e.g. behind the site login of the plugin's session).
/// </summary>
public static class TorrentDataUrl
{
    private const string Prefix = "data:application/x-bittorrent;base64,";

    public static bool IsTorrentDataUrl(string url) => url.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The link for logs: a torrent data: URL is hundreds of KB.</summary>
    public static string ForLog(string url) => IsTorrentDataUrl(url) ? $"(.torrent data, {url.Length - Prefix.Length} chars)" : url;

    /// <summary>The .torrent bytes of a torrent data: URL; false for any other link or invalid base64.</summary>
    public static bool TryDecode(string url, out byte[] bytes)
    {
        bytes = [];
        if (!IsTorrentDataUrl(url)) return false;
        try
        {
            bytes = Convert.FromBase64String(url[Prefix.Length..]);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
