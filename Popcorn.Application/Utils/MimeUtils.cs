
namespace Popcorn.Domain.Utils;

/// <summary>
/// Class for getting mime types. Most probably will only be used for finding default programs.
/// </summary>
public static class MimeUtils
{
    private const string DefaultContentType = "application/octet-stream";
    private static readonly Dictionary<string, string> _mimeMappings = new()
    {
        {".torrent", "application/x-bittorrent"},
        {".mp4", "application/mp4"},
    };

    public static string GetMimeTypeForFileExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return _mimeMappings.GetValueOrDefault(extension, DefaultContentType);
    }
}
