
namespace Popcorn.Shared.Utils;
public static class DirectoryUtils
{
    public static readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Popcorn" + Path.DirectorySeparatorChar);
    /// <summary>
    /// Remuxes, converted audio tracks and subtitles of the web player. Created again on every use: it is a cache the
    /// user may delete (by hand or from the settings) while the server runs.
    /// </summary>
    public static string StreamCache => Ensure(DataFolder + "StreamCache" + Path.DirectorySeparatorChar);
    public static readonly string LogsFolder = Path.Combine(DataFolder, "logs");
    /// <summary>
    /// Seek bar thumbnails, <c>&lt;fileId&gt;-&lt;seconds&gt;.jpg</c>: one cache for the web player (made by the server) and the
    /// native players (made by the player process), both render them with <c>ThumbnailRenderer</c>.
    /// </summary>
    public static string ThumbnailsFolder => Ensure(Path.Combine(DataFolder, "ThumbnailsCache"));
    /// <summary>
    /// Thumbnails being rendered, moved into <see cref="ThumbnailsFolder"/> once complete (same file system, so the move
    /// is a rename): the server and the players read the cache while the other may be writing the same thumbnail.
    /// </summary>
    public static string ThumbnailsRenderingFolder => Ensure(Path.Combine(ThumbnailsFolder, "rendering"));
    /// <summary>Where the native players kept their thumbnails before they shared <see cref="ThumbnailsFolder"/>.</summary>
    public static readonly string LegacyPlayerThumbnailsFolder = Path.Combine(DataFolder, "thumb");

    public static void EnsureApplicationFolder()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(StreamCache);
        Directory.CreateDirectory(ThumbnailsFolder);
    }

    /// <summary>The folder, made if it is missing (a no-op when it exists).</summary>
    private static string Ensure(string folder)
    {
        Directory.CreateDirectory(folder);
        return folder;
    }
}
