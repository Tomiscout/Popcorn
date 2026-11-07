using MonoTorrent;
using MonoTorrent.PiecePicking;

namespace Popcorn.Downloads;

/// <summary>
/// Piece picking for watching while downloading. MonoTorrent's <see cref="StreamingPieceRequester"/> downloads a
/// window of pieces starting at a "high priority" piece; this moves that window:
/// <list type="number">
/// <item>a player is reading (or just seeked): the first missing piece at or after its position;</item>
/// <item>otherwise, per wanted file (higher <see cref="ITorrentManagerFile.Priority"/> first): its first piece, then its last pieces (<see cref="TailBytes"/>: container indexes
/// such as MKV cues / MP4 moov that players read before playing), then the first missing piece (sequential).</item>
/// </list>
/// Pieces of skipped files (<see cref="Priority.DoNotDownload"/>) are never targeted.
/// </summary>
public sealed class SequentialPieceRequester : IStreamingPieceRequester
{
    public const long TailBytes = 8L * 1024 * 1024;
    /// <summary>A reader that has not read for this long no longer steers the download.</summary>
    private static readonly TimeSpan ReaderTimeout = TimeSpan.FromSeconds(20);
    private const long RetargetIntervalMs = 250;

    private sealed record Reader(ITorrentManagerFile File, long Position, DateTime At);

    private readonly StreamingPieceRequester _inner = new();
    private IPieceRequesterData? _data;
    private ReadOnlyBitField[] _haves = [];
    private volatile Reader? _reader;
    private long _lastRetarget;
    private int _target = -1;

    public bool InEndgameMode => _inner.InEndgameMode;

    public void Initialise(IPieceRequesterData torrentData, IMessageEnqueuer enqueuer, ReadOnlySpan<ReadOnlyBitField> ignoringBitfields)
    {
        _data = torrentData;
        // Live views (our pieces, pieces being hashed, ...): a piece in any of them needs no request.
        _haves = ignoringBitfields.ToArray();
        _target = -1;
        _inner.Initialise(torrentData, enqueuer, ignoringBitfields);
    }

    public void AddRequests(ReadOnlySpan<(IRequester Peer, ReadOnlyBitField Available)> peers)
    {
        Retarget();
        _inner.AddRequests(peers);
    }

    public void AddRequests(IRequester peer, ReadOnlyBitField available, ReadOnlySpan<ReadOnlyBitField> allPeers)
    {
        Retarget();
        _inner.AddRequests(peer, available, allPeers);
    }

    public void SeekToPosition(ITorrentManagerFile file, long position)
    {
        _reader = new Reader(file, position, DateTime.UtcNow);
        // Lets the inner requester cancel requests outside the new window.
        _inner.SeekToPosition(file, position);
        _target = -1;
        _lastRetarget = 0;
    }

    public void ReadToPosition(ITorrentManagerFile file, long position) =>
        _reader = new Reader(file, position, DateTime.UtcNow);

    /// <summary>The file a player is reading (it steers the download), null = none.</summary>
    public ITorrentManagerFile? ReaderFile => _reader is { } r && DateTime.UtcNow - r.At < ReaderTimeout ? r.File : null;

    private void Retarget()
    {
        var data = _data;
        if (data is null) return;
        var now = Environment.TickCount64;
        if (now - _lastRetarget < RetargetIntervalMs) return;
        _lastRetarget = now;

        var target = FindTarget(data);
        if (target < 0 || target == _target) return;
        _target = target;

        var file = data.Files.FirstOrDefault(f => f.StartPieceIndex <= target && target <= f.EndPieceIndex && f.Priority != Priority.DoNotDownload)
                   ?? data.Files.First(f => f.StartPieceIndex <= target && target <= f.EndPieceIndex);
        var position = Math.Max(0, (long)target * data.PieceLength - file.OffsetInTorrent);
        _inner.ReadToPosition(file, position);
    }

    private int FindTarget(IPieceRequesterData data)
    {
        // Higher priority files first, in torrent order within a priority.
        var wanted = data.Files.Where(f => f.Priority != Priority.DoNotDownload && f.Length > 0).OrderByDescending(f => f.Priority).ToList();

        var reader = _reader;
        if (reader is not null && DateTime.UtcNow - reader.At < ReaderTimeout && reader.File.Priority != Priority.DoNotDownload)
        {
            var from = PieceAt(data, reader.File, reader.Position);
            var missing = FirstMissing(from, reader.File.EndPieceIndex);
            if (missing >= 0) return missing;
        }

        foreach (var file in wanted)
        {
            var head = FirstMissing(file.StartPieceIndex, Math.Min(file.StartPieceIndex + 1, file.EndPieceIndex));
            if (head >= 0) return head;
            var tail = FirstMissing(PieceAt(data, file, file.Length - TailBytes), file.EndPieceIndex);
            if (tail >= 0) return tail;
        }

        foreach (var file in wanted)
        {
            var missing = FirstMissing(file.StartPieceIndex, file.EndPieceIndex);
            if (missing >= 0) return missing;
        }
        return -1;
    }

    private static int PieceAt(IPieceRequesterData data, ITorrentManagerFile file, long position) =>
        position >= file.Length
            ? file.EndPieceIndex
            : data.ByteOffsetToPieceIndex(file.OffsetInTorrent + Math.Max(0, position));

    private int FirstMissing(int start, int end)
    {
        for (var i = start; i <= end; i++)
        {
            var have = false;
            foreach (var bitfield in _haves)
            {
                if (i < bitfield.Length && bitfield[i]) { have = true; break; }
            }
            if (!have) return i;
        }
        return -1;
    }

    public bool ValidatePiece(IRequester peer, PieceSegment blockInfo, out bool pieceComplete, HashSet<IRequester> peersInvolved) =>
        _inner.ValidatePiece(peer, blockInfo, out pieceComplete, peersInvolved);

    public bool IsInteresting(IRequester peer, ReadOnlyBitField bitfield) => _inner.IsInteresting(peer, bitfield);

    public void CancelRequests(IRequester peer, int startIndex, int endIndex) => _inner.CancelRequests(peer, startIndex, endIndex);

    public void RequestRejected(IRequester peer, PieceSegment blockInfo) => _inner.RequestRejected(peer, blockInfo);

    public int CurrentRequestCount() => _inner.CurrentRequestCount();
}
