using FFMpegCore;

namespace Popcorn.Shared.Ffmpeg;

/// <summary>
/// Which ffmpeg/ffprobe Popcorn runs: the bundled ones (Popcorn.Installer puts them next to Popcorn.Server, listed in
/// <c>ffmpeg-files.txt</c>) when they are there, else whatever is on PATH. Never changes PATH, neither ours nor the
/// system's. Call <see cref="Configure"/> once at start-up, before anything uses FFMpegCore or <see cref="Ffmpeg"/>.
/// </summary>
public static class FfmpegBinaries
{
    /// <summary>Folder with the bundled binaries; null = taken from PATH.</summary>
    public static string? Folder { get; private set; }

    /// <summary>ffmpeg to start (full path, or the bare name for PATH).</summary>
    public static string Ffmpeg => Folder is null ? "ffmpeg" : Path.Combine(Folder, ExeName("ffmpeg"));

    /// <summary>
    /// Takes the first of <paramref name="folders"/> that has both ffmpeg and ffprobe (default: this program's
    /// folder) for FFMpegCore and <see cref="Ffmpeg"/>.
    /// </summary>
    public static string? Configure(params string[] folders)
    {
        if (folders.Length == 0)
            folders = [AppContext.BaseDirectory];

        Folder = folders.Select(Path.GetFullPath).FirstOrDefault(folder =>
            File.Exists(Path.Combine(folder, ExeName("ffmpeg"))) &&
            File.Exists(Path.Combine(folder, ExeName("ffprobe"))));
        if (Folder is not null)
            GlobalFFOptions.Configure(options => options.BinaryFolder = Folder);
        return Folder;
    }

    private static string ExeName(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;
}
