

public interface INativeFileAssosiation {
    string? GetExecCommandForExtention(string ext, string filePath, string args);

    /// <summary>Display name of the program the OS opens video files with, null if none is associated.</summary>
    string? GetDefaultVideoPlayerName();
}