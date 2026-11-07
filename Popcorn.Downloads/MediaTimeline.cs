using System.Buffers.Binary;
using System.Text;

namespace Popcorn.Downloads;

/// <summary>
/// Maps file offsets to playback time from a container's index (Matroska/WebM Cues, MP4 sample tables). Bitrate varies,
/// so a byte fraction is not a time fraction: seek bars must not show downloaded byte ranges directly.
/// </summary>
public sealed class MediaTimeline
{
    private const int MaxSmallElement = 1024 * 1024;
    private const int MaxIndex = 128 * 1024 * 1024;

    private const uint EbmlHeader = 0x1A45DFA3, Segment = 0x18538067, Cluster = 0x1F43B675, SeekHead = 0x114D9B74,
        Seek = 0x4DBB, SeekId = 0x53AB, SeekPosition = 0x53AC, Info = 0x1549A966, TimecodeScale = 0x2AD7B1,
        Duration = 0x4489, Cues = 0x1C53BB6B, CuePoint = 0xBB, CueTime = 0xB3, CueTrackPositions = 0xB7,
        CueClusterPosition = 0xF1;

    private readonly long[] _offsets;
    private readonly double[] _fractions;

    private MediaTimeline(long[] offsets, double[] fractions)
    {
        _offsets = offsets;
        _fractions = fractions;
    }

    /// <summary>
    /// Reads the index of a file that may still be downloading. <c>Final</c> is false while the parts holding it are
    /// missing (try again later); a final null timeline means the format has no usable index.
    /// </summary>
    public static async Task<(bool Final, MediaTimeline? Timeline)> ReadAsync(IStreamSource source)
    {
        try
        {
            var head = await ReadAt(source, 0, 12);
            if (head.Length < 12) return (false, null);
            if (BinaryPrimitives.ReadUInt32BigEndian(head) == EbmlHeader) return (true, await ReadMatroska(source));
            if (Encoding.ASCII.GetString(head, 4, 4) == "ftyp") return (true, await ReadMp4(source));
            return (true, null);
        }
        catch (MissingDataException)
        {
            return (false, null);
        }
    }

    /// <summary>The share of the playback time reached at a file offset, 0..1.</summary>
    public double FractionAt(long offset)
    {
        var index = Array.BinarySearch(_offsets, offset);
        if (index >= 0) return _fractions[index];
        index = ~index;
        if (index == 0) return _fractions[0];
        if (index >= _offsets.Length) return _fractions[^1];
        var position = (double)(offset - _offsets[index - 1]) / (_offsets[index] - _offsets[index - 1]);
        return _fractions[index - 1] + position * (_fractions[index] - _fractions[index - 1]);
    }

    /// <summary>Downloaded ranges as fractions of the file to fractions of the playback time.</summary>
    public IReadOnlyList<double[]> ToTimeRanges(IEnumerable<double[]> byteRanges, long length)
    {
        var result = new List<double[]>();
        foreach (var range in byteRanges)
        {
            var start = FractionAt((long)(range[0] * length));
            var end = FractionAt((long)(range[1] * length));
            // Headers and trailing indexes hold no playback time.
            if (end > start) result.Add([start, end]);
        }
        return result;
    }

    /// <param name="points">Where playback times (seconds) start in the file.</param>
    /// <param name="trailer">Start of an index stored behind the media data, if any.</param>
    private static MediaTimeline? Build(List<(long Offset, double Time)> points, double duration, long trailer, long length)
    {
        if (points.Count == 0) return null;
        points.Sort();
        var latest = points.Max(p => p.Time);
        duration = Math.Max(duration, latest);
        if (duration <= 0) return null;

        var offsets = new List<long>(points.Count + 3) { 0 };
        var fractions = new List<double>(points.Count + 3) { 0 };
        foreach (var (offset, time) in points)
        {
            if (offset <= offsets[^1]) continue; // earliest time of a cluster
            offsets.Add(offset);
            fractions.Add(Math.Max(fractions[^1], time / duration));
        }
        if (trailer > offsets[^1] && trailer < length)
        {
            offsets.Add(trailer);
            fractions.Add(1);
        }
        offsets.Add(Math.Max(length, offsets[^1] + 1));
        fractions.Add(1);
        return new MediaTimeline(offsets.ToArray(), fractions.ToArray());
    }

    #region Matroska

    private static async Task<MediaTimeline?> ReadMatroska(IStreamSource source)
    {
        var header = await ReadElementHeader(source, 0);
        if (header.Size < 0) return null;
        var segment = await ReadElementHeader(source, header.Data + header.Size);
        if (segment.Id != Segment) return null;
        var segmentEnd = segment.Size < 0 ? source.Length : Math.Min(segment.Data + segment.Size, source.Length);

        // Info and Cues are found on the way to the first cluster or through the SeekHead (Cues usually are at the end).
        long? infoAt = null, cuesAt = null;
        var position = segment.Data;
        for (var i = 0; i < 128 && position < segmentEnd; i++)
        {
            var element = await ReadElementHeader(source, position);
            if (element.Id == Cluster || element.Size < 0) break;
            if (element.Id == Info) infoAt = position;
            else if (element.Id == Cues) cuesAt = position;
            else if (element.Id == SeekHead && element.Size <= MaxSmallElement)
            {
                var body = await ReadAt(source, element.Data, (int)element.Size);
                foreach (var seek in Children(body, 0, body.Length).Where(c => c.Id == Seek))
                {
                    uint id = 0;
                    long at = -1;
                    foreach (var field in Children(body, seek.Start, seek.Start + seek.Length))
                    {
                        if (field.Id == SeekId) id = (uint)ReadUnsigned(body, field);
                        else if (field.Id == SeekPosition) at = segment.Data + ReadUnsigned(body, field);
                    }
                    if (at < 0) continue;
                    if (id == Info) infoAt ??= at;
                    else if (id == Cues) cuesAt ??= at;
                }
            }
            position = element.Data + element.Size;
        }
        if (infoAt is null || cuesAt is null) return null;

        var info = await ReadElementHeader(source, infoAt.Value);
        if (info.Id != Info || info.Size is < 0 or > MaxSmallElement) return null;
        var infoBody = await ReadAt(source, info.Data, (int)info.Size);
        double scale = 1_000_000, duration = 0;
        foreach (var field in Children(infoBody, 0, infoBody.Length))
        {
            if (field.Id == TimecodeScale) scale = ReadUnsigned(infoBody, field);
            else if (field.Id == Duration) duration = ReadFloat(infoBody, field);
        }
        var toSeconds = scale / 1_000_000_000d;

        var cues = await ReadElementHeader(source, cuesAt.Value);
        if (cues.Id != Cues || cues.Size is < 0 or > MaxIndex) return null;
        var cuesBody = await ReadAt(source, cues.Data, (int)cues.Size);
        var points = new List<(long, double)>();
        foreach (var point in Children(cuesBody, 0, cuesBody.Length).Where(c => c.Id == CuePoint))
        {
            double time = -1;
            foreach (var field in Children(cuesBody, point.Start, point.Start + point.Length))
            {
                if (field.Id == CueTime) time = ReadUnsigned(cuesBody, field) * toSeconds;
                else if (field.Id == CueTrackPositions && time >= 0)
                    foreach (var track in Children(cuesBody, field.Start, field.Start + field.Length))
                        if (track.Id == CueClusterPosition)
                            points.Add((segment.Data + ReadUnsigned(cuesBody, track), time));
            }
        }
        return Build(points, duration * toSeconds, cuesAt.Value, source.Length);
    }

    private static async Task<(uint Id, long Size, long Data)> ReadElementHeader(IStreamSource source, long position)
    {
        var bytes = await ReadAt(source, position, 12);
        var at = 0;
        var id = ReadId(bytes, ref at);
        var size = ReadSize(bytes, ref at);
        return (id, size, position + at);
    }

    private static IEnumerable<(uint Id, int Start, int Length)> Children(byte[] data, int start, int end)
    {
        var at = start;
        while (at < end)
        {
            var id = ReadId(data, ref at);
            var size = ReadSize(data, ref at);
            if (size < 0 || size > end - at) yield break;
            yield return (id, at, (int)size);
            at += (int)size;
        }
    }

    private static uint ReadId(byte[] data, ref int at)
    {
        var length = System.Numerics.BitOperations.LeadingZeroCount((uint)data[at]) - 24 + 1;
        if (length > 4) throw new InvalidDataException("Bad EBML id");
        uint id = 0;
        for (var i = 0; i < length; i++) id = id << 8 | data[at + i];
        at += length;
        return id;
    }

    /// <summary>-1 = unknown size.</summary>
    private static long ReadSize(byte[] data, ref int at)
    {
        var length = System.Numerics.BitOperations.LeadingZeroCount((uint)data[at]) - 24 + 1;
        if (length > 8) throw new InvalidDataException("Bad EBML size");
        long size = data[at] & (0xFF >> length);
        var unknown = size == 0xFF >> length;
        for (var i = 1; i < length; i++)
        {
            size = size << 8 | data[at + i];
            unknown &= data[at + i] == 0xFF;
        }
        at += length;
        return unknown ? -1 : size;
    }

    private static long ReadUnsigned(byte[] data, (uint Id, int Start, int Length) element)
    {
        long value = 0;
        for (var i = 0; i < element.Length; i++) value = value << 8 | data[element.Start + i];
        return value;
    }

    private static double ReadFloat(byte[] data, (uint Id, int Start, int Length) element) => element.Length switch
    {
        4 => BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(element.Start, 4)),
        8 => BinaryPrimitives.ReadDoubleBigEndian(data.AsSpan(element.Start, 8)),
        _ => 0,
    };

    #endregion

    #region MP4

    private static async Task<MediaTimeline?> ReadMp4(IStreamSource source)
    {
        long position = 0;
        while (position + 8 <= source.Length)
        {
            var header = await ReadAt(source, position, 16);
            var (type, size, data) = ReadBoxHeader(header, position, source.Length);
            if (size < 8) return null;
            if (type == "moov")
            {
                if (size - (data - position) > MaxIndex) return null;
                var moov = await ReadAt(source, data, (int)(size - (data - position)));
                return ParseMoov(moov, position, source.Length);
            }
            position += size;
        }
        return null;
    }

    private static MediaTimeline? ParseMoov(byte[] moov, long moovAt, long length)
    {
        foreach (var trak in Boxes(moov, 0, moov.Length).Where(b => b.Type == "trak"))
        {
            var mdia = Find(moov, trak, "mdia");
            var hdlr = mdia is { } a ? Find(moov, a, "hdlr") : null;
            if (hdlr is not { } handler || Encoding.ASCII.GetString(moov, handler.Start + 8, 4) != "vide") continue;
            if (Find(moov, mdia!.Value, "mdhd") is not { } mdhd) continue;
            var timescale = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(mdhd.Start + (moov[mdhd.Start] == 1 ? 20 : 12)));
            if (timescale == 0) continue;
            if (Find(moov, mdia.Value, "minf") is not { } minf || Find(moov, minf, "stbl") is not { } stbl) continue;
            return Find(moov, stbl, "stts") is { } stts && Find(moov, stbl, "stsc") is { } stsc
                ? BuildFromSampleTables(moov, stts, stsc, Find(moov, stbl, "stco"), Find(moov, stbl, "co64"), timescale, moovAt, length)
                : null;
        }
        return null;
    }

    private static MediaTimeline? BuildFromSampleTables(byte[] moov, Box stts, Box stsc, Box? stco, Box? co64, uint timescale, long moovAt, long length)
    {
        var chunkTable = stco ?? co64;
        if (chunkTable is not { } chunks) return null; // fragmented files keep theirs in the moofs
        var wide = stco is null;
        var chunkCount = U32(moov, chunks.Start + 4);
        var scEntries = U32(moov, stsc.Start + 4);
        var sttsEntries = U32(moov, stts.Start + 4);
        if (scEntries == 0 || sttsEntries == 0) return null;

        var points = new List<(long, double)>((int)Math.Min(chunkCount, 1 << 20));
        uint scIndex = 0, sttsIndex = 0, sttsUsed = 0;
        long ticks = 0;
        for (uint chunk = 0; chunk < chunkCount; chunk++)
        {
            var offset = wide
                ? (long)BinaryPrimitives.ReadUInt64BigEndian(moov.AsSpan(chunks.Start + 8 + (int)chunk * 8))
                : U32(moov, chunks.Start + 8 + (int)chunk * 4);
            points.Add((offset, (double)ticks / timescale));

            while (scIndex + 1 < scEntries && U32(moov, stsc.Start + 8 + (int)(scIndex + 1) * 12) <= chunk + 1) scIndex++;
            var samples = U32(moov, stsc.Start + 8 + (int)scIndex * 12 + 4);
            while (samples > 0 && sttsIndex < sttsEntries)
            {
                var count = U32(moov, stts.Start + 8 + (int)sttsIndex * 8);
                var delta = U32(moov, stts.Start + 8 + (int)sttsIndex * 8 + 4);
                var take = Math.Min(samples, count - sttsUsed);
                ticks += (long)take * delta;
                samples -= take;
                sttsUsed += take;
                if (sttsUsed >= count)
                {
                    sttsIndex++;
                    sttsUsed = 0;
                }
            }
        }
        return Build(points, (double)ticks / timescale, moovAt, length);
    }

    private readonly record struct Box(string Type, int Start, int Length);

    private static (string Type, long Size, long Data) ReadBoxHeader(byte[] header, long position, long fileLength)
    {
        long size = BinaryPrimitives.ReadUInt32BigEndian(header);
        var type = Encoding.ASCII.GetString(header, 4, 4);
        var data = position + 8;
        if (size == 1)
        {
            if (header.Length < 16) return (type, 0, data);
            size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
            data += 8;
        }
        else if (size == 0) size = fileLength - position;
        return (type, size, data);
    }

    private static IEnumerable<Box> Boxes(byte[] data, int start, int end)
    {
        var at = start;
        while (at + 8 <= end)
        {
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
            var type = Encoding.ASCII.GetString(data, at + 4, 4);
            var headerSize = 8;
            if (size == 1)
            {
                if (at + 16 > end) yield break;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(at + 8));
                headerSize = 16;
            }
            else if (size == 0) size = end - at;
            if (size < headerSize || size > end - at) yield break;
            yield return new Box(type, at + headerSize, (int)size - headerSize);
            at += (int)size;
        }
    }

    private static Box? Find(byte[] data, Box parent, string type)
    {
        foreach (var box in Boxes(data, parent.Start, parent.Start + parent.Length))
            if (box.Type == type) return box;
        return null;
    }

    private static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));

    #endregion

    /// <summary>Up to <paramref name="count"/> bytes, fewer only at the end of the file; missing data throws.</summary>
    private static async Task<byte[]> ReadAt(IStreamSource source, long position, int count)
    {
        count = (int)Math.Min(count, source.Length - position);
        if (count <= 0) return [];
        if (!source.Has(position, count)) throw new MissingDataException();
        var buffer = new byte[count];
        var filled = 0;
        while (filled < count)
        {
            var read = await source.ReadAsync(position + filled, buffer.AsMemory(filled), CancellationToken.None);
            if (read <= 0) throw new MissingDataException();
            filled += read;
        }
        return buffer;
    }

    private sealed class MissingDataException : Exception;
}
