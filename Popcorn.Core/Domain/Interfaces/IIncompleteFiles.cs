namespace Popcorn.Domain.Interfaces;

/// <summary>
/// Files on disk that are not fully written yet (downloads in progress, paused or with skipped parts): their missing
/// parts read as zeros, so anything that decodes a whole file (segment finder) has to leave them alone.
/// Implemented by <c>Popcorn.Downloads.DownloadManager</c>.
/// </summary>
public interface IIncompleteFiles
{
    /// <summary>Full paths of the incomplete files.</summary>
    /// <param name="waitForStart">Wait until the downloads are restored on start-up; else nothing counts as incomplete until then.</param>
    Task<IReadOnlySet<string>> GetIncompleteFilesAsync(bool waitForStart);
}
