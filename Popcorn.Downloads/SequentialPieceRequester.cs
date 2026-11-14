using MonoTorrent;
using MonoTorrent.PiecePicking;

namespace Popcorn.Downloads;

/// <summary>
/// Piece picking for watching while downloading: one file at a time is streamed (pieces in order), everything else is
/// picked the BitTorrent way (file priority, then rarest first, randomised).
/// <para>The streamed files, in order of urgency:</para>
/// <list type="number">
/// <item>the file a player reads (or just seeked in), from its position; its first two missing pieces may be requested
/// from several peers at once;</item>
/// <item>the next episode, once the player is near the end of the current one (<see cref="SetNextFile"/>);</item>
/// <item>with no player reading: the file streamed last, else the first wanted video file (its head, its last
/// <see cref="TailBytes"/> for the container index players read first, then the rest in order).</item>
/// </list>
/// Unlike MonoTorrent's <see cref="StreamingPieceRequester"/>, which allows only 2–4 outstanding blocks per peer (so a
/// peer's speed is bound by its round-trip time), every peer's request queue is filled. Both pickers share one
/// <see cref="StandardPicker"/>, which tracks the requests in flight. Pieces of skipped files
/// (<see cref="Priority.DoNotDownload"/>) are never streamed.
/// </summary>
public sealed class SequentialPieceRequester(Func<ITorrentManagerFile, bool> isVideo) : IStreamingPieceRequester
{
    public const long TailBytes = 8L * 1024 * 1024;
    /// <summary>A reader that has not read for this long no longer steers the download.</summary>
    private static readonly TimeSpan ReaderTimeout = TimeSpan.FromSeconds(20);
    private const long RetargetIntervalMs = 250;
    /// <summary>After a seek, requests outside this many pieces from the new position are cancelled.</summary>
    private const int SeekWindow = 30;

    private sealed record Reader(ITorrentManagerFile File, long Position, DateTime At);

    /// <summary>A piece range to stream; <c>Urgent</c> pieces may be requested from more than one peer.</summary>
    private readonly record struct Range(int Start, int End, bool Urgent);

    private IPieceRequesterData? _data;
    private IMessageEnqueuer? _enqueuer;
    private StandardPicker? _standard;
    private IPiecePicker? _optimal;
    private ReadOnlyBitField[] _ignoring = [];
    private BitField? _filtered;
    private Memory<PieceSegment> _requestBuffer = Memory<PieceSegment>.Empty;
    private PieceSegment[] _cancellations = [];
    private ReadOnlyBitField[] _otherAvailable = [];

    private volatile Reader? _reader;
    /// <summary>The file a player read last; streamed while no player reads.</summary>
    private volatile Reader? _lastStreamed;
    private volatile ITorrentManagerFile? _next;
    private volatile bool _seekPending;
    private long _lastRetarget;
    private Range[] _ranges = [];

    public bool InEndgameMode { get; private set; }

    public void Initialise(IPieceRequesterData torrentData, IMessageEnqueuer enqueuer, ReadOnlySpan<ReadOnlyBitField> ignoringBitfields)
    {
        _data = torrentData;
        _enqueuer = enqueuer;
        // Live views (our pieces, pieces being hashed, ...): a piece in any of them needs no request.
        _ignoring = ignoringBitfields.ToArray();
        _filtered = new BitField(torrentData.PieceCount);
        _standard = new StandardPicker();
        _optimal = new PriorityPicker(new RarestFirstPicker(new RandomisedPicker(_standard)));
        _standard.Initialise(torrentData);
        _optimal.Initialise(torrentData);
        _lastRetarget = 0;
        _ranges = [];
    }

    public void SeekToPosition(ITorrentManagerFile file, long position)
    {
        _reader = _lastStreamed = new Reader(file, position, DateTime.UtcNow);
        _seekPending = true;
        _lastRetarget = 0;
    }

    public void ReadToPosition(ITorrentManagerFile file, long position)
    {
        if (_reader?.File != file) _lastRetarget = 0;
        _reader = _lastStreamed = new Reader(file, position, DateTime.UtcNow);
    }

    /// <summary>The file a player is reading (it steers the download), null = none.</summary>
    public ITorrentManagerFile? ReaderFile => ActiveReader?.File;

    /// <summary>The player's position in <see cref="ReaderFile"/>.</summary>
    public long ReaderPosition => ActiveReader?.Position ?? 0;

    private Reader? ActiveReader => _reader is { } r && DateTime.UtcNow - r.At < ReaderTimeout ? r : null;

    /// <summary>Streamed right after the file a player reads (next episode near the end of this one); null = none.</summary>
    public void SetNextFile(ITorrentManagerFile? file)
    {
        if (_next == file) return;
        _next = file;
        _lastRetarget = 0;
    }

    public void AddRequests(ReadOnlySpan<(IRequester Peer, ReadOnlyBitField Available)> peers)
    {
        if (_data is null) return;
        Retarget();

        if (_otherAvailable.Length < peers.Length) _otherAvailable = new ReadOnlyBitField[peers.Length];
        var otherAvailable = _otherAvailable.AsSpan(0, peers.Length);
        for (var i = 0; i < peers.Length; i++) otherAvailable[i] = peers[i].Available;

        if (_seekPending && _ranges.Length > 0)
        {
            // Queues are deep: drop what was requested away from the new position, so the seek target comes first.
            _seekPending = false;
            var start = _ranges[0].Start;
            var end = Math.Min(_data.PieceCount - 1, start + SeekWindow);
            foreach (var (peer, _) in peers)
            {
                if (!peer.CanCancelRequests) continue;
                if (start > 0) CancelRequests(peer, 0, start - 1);
                if (end < _data.PieceCount - 1) CancelRequests(peer, end + 1, _data.PieceCount - 1);
            }
            foreach (var (peer, available) in peers) AddRequests(peer, available, otherAvailable);
            return;
        }

        foreach (var (peer, available) in peers)
        {
            if (peer.SuggestedPieces.Count > 0 || (!peer.IsChoking && peer.AmRequestingPiecesCount == 0))
                AddRequests(peer, available, otherAvailable);
        }
    }

    public void AddRequests(IRequester peer, ReadOnlyBitField available, ReadOnlySpan<ReadOnlyBitField> allPeers)
    {
        if (!peer.CanRequestMorePieces || _data is null || _standard is null || _optimal is null || _enqueuer is null) return;
        Retarget();
        var last = _data.PieceCount - 1;

        // Finish pieces this peer started (a choking peer can only continue its allowed-fast pieces).
        if (!peer.IsChoking || peer.SupportsFastPeer)
        {
            while (peer.CanRequestMorePieces && _standard.ContinueExistingRequest(peer, 0, last, out var segment))
                _enqueuer.EnqueueRequest(peer, segment);
        }

        if (peer.IsChoking && !(peer.SupportsFastPeer && peer.IsAllowedFastPieces.Count > 0)) return;

        var filtered = Filter(available);
        var count = peer.PreferredRequestAmount(_data.PieceLength);
        if (_requestBuffer.Length < count) _requestBuffer = new PieceSegment[count];

        foreach (var range in _ranges)
        {
            if (!peer.CanRequestMorePieces) return;
            Pick(peer, _standard, filtered, allPeers, range.Start, range.End, count);
            if (range.Urgent && !peer.IsChoking)
            {
                while (peer.CanRequestMorePieces && _standard.ContinueAnyExistingRequest(peer, filtered, range.Start, range.End, 2, out var segment))
                    _enqueuer.EnqueueRequest(peer, segment);
            }
        }

        Pick(peer, _optimal, filtered, allPeers, 0, last, count);

        if (!peer.IsChoking && peer.AmRequestingPiecesCount == 0)
        {
            while (peer.CanRequestMorePieces)
            {
                if (_optimal.ContinueAnyExistingRequest(peer, filtered, 0, last, 1, out var segment))
                    _enqueuer.EnqueueRequest(peer, segment);
                else if ((InEndgameMode || available.AllTrue) && _optimal.ContinueAnyExistingRequest(peer, filtered, 0, last, 2, out segment))
                {
                    InEndgameMode = true;
                    _enqueuer.EnqueueRequest(peer, segment);
                }
                else break;
            }
        }
    }

    private void Pick(IRequester peer, IPiecePicker picker, ReadOnlyBitField available, ReadOnlySpan<ReadOnlyBitField> allPeers, int start, int end, int count)
    {
        var buffer = _requestBuffer.Span[..count];
        while (peer.CanRequestMorePieces)
        {
            var requested = picker.PickPiece(peer, available, allPeers, start, end, buffer);
            if (requested <= 0) return;
            _enqueuer!.EnqueueRequests(peer, buffer[..requested]);
        }
    }

    /// <summary>The peer's pieces minus the ones we have or are hashing.</summary>
    private ReadOnlyBitField Filter(ReadOnlyBitField available)
    {
        _filtered!.From(available);
        foreach (var ignoring in _ignoring) _filtered.NAnd(ignoring);
        return _filtered;
    }

    private void Retarget()
    {
        var data = _data;
        if (data is null) return;
        var now = Environment.TickCount64;
        if (now - _lastRetarget < RetargetIntervalMs) return;
        _lastRetarget = now;
        _ranges = BuildRanges(data);
    }

    private Range[] BuildRanges(IPieceRequesterData data)
    {
        var ranges = new List<Range>();
        var reader = ActiveReader;
        if (reader is not null && reader.File.Priority != Priority.DoNotDownload)
        {
            var first = FirstMissing(PieceAt(data, reader.File, reader.Position), reader.File.EndPieceIndex);
            if (first >= 0)
            {
                ranges.Add(new Range(first, Math.Min(first + 1, reader.File.EndPieceIndex), true));
                ranges.Add(new Range(first, reader.File.EndPieceIndex, false));
            }
            if (_next is { } next && next != reader.File && next.Priority != Priority.DoNotDownload)
                AddWholeFile(data, ranges, next);
            return [.. ranges];
        }

        var streamed = _lastStreamed is { } s && s.File.Priority != Priority.DoNotDownload ? s.File : null;
        if (streamed is not null && FirstMissing(streamed.StartPieceIndex, streamed.EndPieceIndex) >= 0)
        {
            // Continue where the player stopped, then the rest of the file.
            ranges.Add(new Range(PieceAt(data, streamed, _lastStreamed!.Position), streamed.EndPieceIndex, false));
            AddWholeFile(data, ranges, streamed);
            return [.. ranges];
        }

        var wanted = data.Files.Where(f => f.Priority != Priority.DoNotDownload && f.Length > 0).ToList();
        if ((wanted.FirstOrDefault(isVideo) ?? wanted.FirstOrDefault()) is { } firstFile)
            AddWholeFile(data, ranges, firstFile);
        return [.. ranges];
    }

    /// <summary>Head piece, container index at the end, then everything in order.</summary>
    private static void AddWholeFile(IPieceRequesterData data, List<Range> ranges, ITorrentManagerFile file)
    {
        ranges.Add(new Range(file.StartPieceIndex, Math.Min(file.StartPieceIndex + 1, file.EndPieceIndex), false));
        ranges.Add(new Range(PieceAt(data, file, file.Length - TailBytes), file.EndPieceIndex, false));
        ranges.Add(new Range(file.StartPieceIndex, file.EndPieceIndex, false));
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
            foreach (var bitfield in _ignoring)
            {
                if (i < bitfield.Length && bitfield[i]) { have = true; break; }
            }
            if (!have) return i;
        }
        return -1;
    }

    public bool ValidatePiece(IRequester peer, PieceSegment blockInfo, out bool pieceComplete, HashSet<IRequester> peersInvolved)
    {
        pieceComplete = false;
        return _standard is not null && _standard.ValidatePiece(peer, blockInfo, out pieceComplete, peersInvolved);
    }

    public bool IsInteresting(IRequester peer, ReadOnlyBitField bitfield) =>
        _optimal is not null && _optimal.IsInteresting(peer, bitfield);

    public void CancelRequests(IRequester peer, int startIndex, int endIndex)
    {
        if (_standard is null || _enqueuer is null) return;
        if (_cancellations.Length < peer.AmRequestingPiecesCount) _cancellations = new PieceSegment[peer.AmRequestingPiecesCount];
        var cancellations = _cancellations.AsSpan(0, peer.AmRequestingPiecesCount);
        var cancelled = _standard.CancelRequests(peer, startIndex, endIndex, cancellations);
        _enqueuer.EnqueueCancellations(peer, cancellations[..cancelled]);
    }

    public void RequestRejected(IRequester peer, PieceSegment blockInfo) => _standard?.RequestRejected(peer, blockInfo);

    public int CurrentRequestCount() => _standard?.CurrentRequestCount() ?? 0;
}
