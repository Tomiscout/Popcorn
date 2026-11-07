using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Popcorn.Domain.Enum;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Shared.Utils;

namespace Popcorn.SegmentFinder;

/// <summary>
/// Intro/outro detection for a media item, in two phases:
/// <list type="number">
/// <item>Detect: the heads and tails of 2 to <see cref="MaxDetectFiles"/> files are fingerprinted against each other;
/// audio shared by any two of them is saved right away as a <see cref="SegmentInfo"/> reference of the guessed type (intro from the head, outro from the
/// tail). The user can change its type or delete it.</item>
/// <item>Scan: every file is matched against the references (and its intro / outro named chapters). For each type
/// a file has no saved segment of, the scan's pick is saved as <see cref="SegmentLocation"/>s (outros relative to
/// the end of the file, negative start); everything else found stays in memory as alternatives the user can switch
/// to (<see cref="ApplyAsync"/>).</item>
/// </list>
/// Files without a match stay visible as "not found"; running Detect on some of them (e.g. a season whose intro
/// changed) adds references, which marks every file without a location of that type as not scanned.
/// Scan alternatives and job progress live in memory only; the UI polls <see cref="GetStateAsync"/>.
/// </summary>
public class SegmentFinderService
{
    public static readonly string WorkDirectory = Path.Combine(DirectoryUtils.DataFolder, "segmentFinder");
    /// <summary>Leftover of older versions (unsaved candidates), deleted on start.</summary>
    private static readonly string CandidateDirectory = Path.Combine(WorkDirectory, "candidates");
    private static readonly string TempDirectory = Path.Combine(WorkDirectory, "tmp");

    /// <summary>Most files one detection compares; the dialog has the same limit.</summary>
    public const int MaxDetectFiles = 5;
    /// <summary>Matches closer than this are one segment (dialogue over the intro song splits matches).</summary>
    private const double MergeGapSeconds = 3;
    /// <summary>When scanning, at least this part of a reference has to match.</summary>
    private const double MinReferenceCoverage = 0.5;

    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly ILogger<SegmentFinderService> _logger;
    private readonly ConcurrentDictionary<Guid, ItemState> _states = new();
    private readonly SemaphoreSlim _chapterReferenceLock = new(1, 1);
    private static readonly SegmentType[] SegmentTypes = [SegmentType.Intro, SegmentType.Outro];

    public SegmentFinderService(IMediaItemRepository mediaItemRepository, IMediaFileRepository mediaFileRepository,
        ILogger<SegmentFinderService> logger)
    {
        _mediaItemRepository = mediaItemRepository;
        _mediaFileRepository = mediaFileRepository;
        _logger = logger;

        if (Directory.Exists(CandidateDirectory)) Directory.Delete(CandidateDirectory, true);
        if (Directory.Exists(TempDirectory)) Directory.Delete(TempDirectory, true);
        Directory.CreateDirectory(TempDirectory);
    }

    public async Task<SegmentsState?> GetStateAsync(Guid itemId)
    {
        var item = await _mediaItemRepository.GetMediaItemFull(itemId);
        if (item is null) return null;

        var segments = item.Segments.ToDictionary(s => s.Id);
        var ordered = MediaFiles(item);
        var references = item.Segments
            .Where(s => !IsChapterReference(s))
            .OrderBy(s => s.Type)
            .Select(s => new SegmentReference(s.Id, s.Type, s.Length,
                AppliedRanges(item, ordered, f => f.SegmentLocations.Any(l => l.SegmentId == s.Id))))
            .ToList();

        var state = GetItemState(itemId);
        lock (state)
        {
            var files = ordered
                .Select(f => new SegmentFileState(f.Id, FileLabel(item, f), RelativeTitle(item, f),
                    f.IntroProcessed, f.OutroProcessed,
                    Options(f, segments, state.Matches.GetValueOrDefault(f.Id) ?? [])))
                .ToList();
            return new SegmentsState(itemId, state.Job, state.Message, state.Error, references, files);
        }
    }

    /// <summary>The file's saved locations merged with what the last scan found in it.</summary>
    private static List<SegmentOption> Options(MediaFile file, Dictionary<Guid, SegmentInfo> segments, List<SegmentMatch> matches)
    {
        var options = new List<SegmentOption>();
        foreach (var location in file.SegmentLocations)
        {
            if (!segments.TryGetValue(location.SegmentId, out var segment)) continue;
            var found = matches.Any(m => m.SegmentId == location.SegmentId
                                         && SameSpan(m.Start, m.Length, location.StartSeconds, location.Length));
            options.Add(new SegmentOption(segment.Id, segment.Type, location.StartSeconds, location.Length,
                IsChapterReference(segment), Applied: true, Found: found));
        }
        foreach (var match in matches)
        {
            if (options.Any(o => o.SegmentId == match.SegmentId && SameSpan(o.Start, o.Length, match.Start, match.Length))) continue;
            options.Add(new SegmentOption(match.SegmentId, match.Type, match.Start, match.Length, match.FromChapter,
                Applied: false, Found: true));
        }
        return options.OrderBy(o => o.Type).ThenBy(o => o.Start).ToList();
    }

    private static bool SameSpan(double startA, double lengthA, double startB, double lengthB) =>
        Math.Abs(startA - startB) < 1 && Math.Abs(lengthA - lengthB) < 1;

    /// <summary>
    /// Sets the segments of the given files (replacing what they had) to the picked ones: references of the item
    /// at a start / length as found by a scan or saved before.
    /// </summary>
    public async Task ApplyAsync(Guid itemId, IReadOnlyDictionary<Guid, List<SegmentPick>> picks)
    {
        var state = GetItemState(itemId);
        lock (state)
        {
            if (state.Job is not null) throw new InvalidOperationException("Wait for the running job to finish");
        }

        var item = await _mediaItemRepository.GetMediaItemWithSegments(itemId)
                   ?? throw new InvalidOperationException("Media item not found");
        var segments = item.Segments.ToDictionary(s => s.Id);
        var fileIds = (await _mediaFileRepository.GetFilesForItemId(itemId)).Select(f => f.Id).ToHashSet();

        foreach (var (fileId, filePicks) in picks)
        {
            if (!fileIds.Contains(fileId)) throw new InvalidOperationException("File is not part of this item");
            var locations = new List<SegmentLocation>();
            foreach (var pick in filePicks)
            {
                if (!segments.TryGetValue(pick.SegmentId, out var segment))
                    throw new InvalidOperationException("Segment is not part of this item");
                // The player tells intros from outros by the sign of the start.
                if (!double.IsFinite(pick.Start) || !double.IsFinite(pick.Length) || pick.Length <= 0
                    || (segment.Type == SegmentType.Intro) != (pick.Start >= 0))
                    throw new InvalidOperationException("Invalid segment position");
                locations.Add(new SegmentLocation
                    { SegmentId = pick.SegmentId, FileId = fileId, StartSeconds = pick.Start, Length = pick.Length });
            }
            await _mediaItemRepository.ReplaceSegmentLocations(fileId, locations);
        }
    }

    /// <summary>Phase 1: finds audio shared by any two of the files. False when a job of the item is already running.</summary>
    public bool StartDetection(Guid itemId, IReadOnlyList<Guid> fileIds)
    {
        return StartJob(itemId, SegmentJobKind.Detect, state => DetectAsync(itemId, fileIds, state));
    }

    /// <summary>Phase 2: matches every file against the references. False when a job is already running.</summary>
    public bool StartScan(Guid itemId)
    {
        return StartJob(itemId, SegmentJobKind.Scan, state => ScanAsync(itemId, state));
    }

    /// <summary>
    /// Changes a reference's type (detection guesses it from where the audio was). Its locations are dropped (the
    /// start's sign is the type) and files are marked for the next scan.
    /// </summary>
    public async Task SetReferenceTypeAsync(Guid itemId, Guid segmentId, SegmentType type)
    {
        var state = GetItemState(itemId);
        lock (state)
        {
            if (state.Job is not null) throw new InvalidOperationException("Wait for the running job to finish");
        }

        var item = await _mediaItemRepository.GetMediaItemWithSegments(itemId);
        var segment = item?.Segments.FirstOrDefault(s => s.Id == segmentId && !IsChapterReference(s));
        if (segment is null || segment.Type == type) return;

        await _mediaItemRepository.SetSegmentType(segmentId, type);
        foreach (var t in SegmentTypes) await _mediaItemRepository.ResetSegmentsProcessed(itemId, t);
        ForgetMatches(state, segmentId);
    }

    /// <summary>Deletes a reference with all its locations; files that lose their segment get scanned again.</summary>
    public async Task DeleteReferenceAsync(Guid itemId, Guid segmentId)
    {
        var state = GetItemState(itemId);
        lock (state)
        {
            if (state.Job is not null) throw new InvalidOperationException("Wait for the running job to finish");
        }

        var item = await _mediaItemRepository.GetMediaItemWithSegments(itemId);
        var segment = item?.Segments.FirstOrDefault(s => s.Id == segmentId && !IsChapterReference(s));
        if (segment is null) return;

        await _mediaItemRepository.DeleteSegmentInfo(segmentId); // cascades to SegmentLocation
        await _mediaItemRepository.ResetSegmentsProcessed(itemId, segment.Type);
        ForgetMatches(state, segmentId);

        DeleteReferenceFiles(segment);
    }

    private static void ForgetMatches(ItemState state, Guid segmentId)
    {
        lock (state)
        {
            foreach (var (fileId, matches) in state.Matches.ToList())
                state.Matches[fileId] = matches.Where(m => m.SegmentId != segmentId).ToList();
        }
    }

    /// <summary>
    /// Forgets a deleted media item: its scan state and the audio of its references (the database rows are gone
    /// with the item through cascades).
    /// </summary>
    public void ForgetItem(Guid itemId, IEnumerable<SegmentInfo> segments)
    {
        _states.TryRemove(itemId, out _);

        foreach (var segment in segments) DeleteReferenceFiles(segment);
    }

    /// <summary>Listening clip of a reference, null when there is none.</summary>
    public static string? GetPreviewPath(Guid segmentId)
    {
        var path = Path.Combine(WorkDirectory, $"{segmentId}.{PreviewExtension}");
        return File.Exists(path) ? path : null;
    }

    public const string PreviewExtension = "ogg";

    private async Task DetectAsync(Guid itemId, IReadOnlyList<Guid> fileIds, ItemState state)
    {
        var item = await _mediaItemRepository.GetMediaItemFull(itemId)
                   ?? throw new InvalidOperationException("Media item not found");
        var files = MediaFiles(item).ToDictionary(f => f.Id);
        if (fileIds.Count is < 2 or > MaxDetectFiles
            || fileIds.Distinct().Count() != fileIds.Count
            || !fileIds.All(files.ContainsKey))
        {
            throw new InvalidOperationException($"Pick 2 to {MaxDetectFiles} different files of this item");
        }

        var picked = new List<(MediaFile File, MediaProbe Probe)>();
        foreach (var id in fileIds)
            picked.Add((files[id], await AudioExtractor.ProbeAsync(files[id].Path)));

        var saved = 0;
        var known = new HashSet<string>();
        var regions = new[] { AudioRegion.Head, AudioRegion.Tail };
        for (var i = 0; i < regions.Length; i++)
        {
            var region = regions[i];
            var type = TypeOf(region);
            var foundAny = false;

            // Audio that references already cover (e.g. an outro shared by all seasons, or what an earlier file of
            // this run gave) is no news.
            var references = await BuildReferencesAsync(AudioReferences(item, type));
            // Every file is compared to the ones before it, so audio shared by any two of them is found.
            var earlier = new Fingerprinter();

            for (var j = 0; j < picked.Count; j++)
            {
                var (file, probe) = picked[j];
                SetJob(state, $"Comparing {(type == SegmentType.Intro ? "beginnings" : "endings")}",
                    i * picked.Count + j, regions.Length * picked.Count);

                var audio = await AudioExtractor.ExtractRegionAsync(file.Path, probe, region, TempFile());
                var found = new List<Span>();
                try
                {
                    var shared = earlier.IsEmpty
                        ? []
                        : Fingerprinter.Merge(await earlier.QueryAsync(audio.Path), MergeGapSeconds)
                            .Where(m => m.Length >= ChapterRules.MinSegmentSeconds)
                            .Select(m => ToSpan(audio, m))
                            .ToList();

                    var covered = references.IsEmpty
                        ? []
                        : (await references.QueryAsync(audio.Path)).Select(m => (m.TrackId, Span: ToSpan(audio, m))).ToList();
                    bool IsKnown(Span span)
                    {
                        var hits = covered.Where(c => c.Span.Overlap(span) >= span.Length / 2).ToList();
                        known.UnionWith(hits.Select(c => c.TrackId));
                        return hits.Count > 0;
                    }

                    // A chapter named like the segment is taken as is, audio overlapping it is the same segment.
                    var named = ChapterRules.Named(probe, type);
                    if (named is not null)
                    {
                        if (!IsKnown(named)) found.Add(named);
                        shared.RemoveAll(m => m.Overlap(named) > 0);
                    }

                    var matches = new List<Span>();
                    foreach (var match in shared)
                    {
                        if (!IsKnown(match)) matches.Add(ChapterRules.Align(probe, match, match.Length));
                    }
                    found.AddRange(ChapterRules.PreferAligned(probe, matches));
                    found.RemoveAll(f => f.Length < ChapterRules.MinSegmentSeconds);

                    await earlier.AddAsync(file.Id.ToString(), audio.Path);
                }
                finally
                {
                    File.Delete(audio.Path);
                }

                foreach (var span in found)
                {
                    var id = Guid.NewGuid();
                    var fileName = $"{id}.wav";
                    var wav = Path.Combine(WorkDirectory, fileName);
                    await AudioExtractor.ExtractFingerprintAsync(file.Path, probe, span.Start, span.Length, wav);
                    await AudioExtractor.ExtractPreviewAsync(file.Path, probe, span.Start, span.Length,
                        Path.ChangeExtension(wav, PreviewExtension));

                    await _mediaItemRepository.AddSegmentInfo(itemId, new SegmentInfo
                    {
                        Id = id, ItemId = itemId, FilePath = fileName, Length = span.Length, Type = type
                    });
                    await references.AddAsync(id.ToString(), wav);
                    saved++;
                    foundAny = true;
                }
            }
            // Files where nothing was found have to be scanned again with the new reference.
            if (foundAny) await _mediaItemRepository.ResetSegmentsProcessed(itemId, type);
        }

        // Only references saved before this run count as already known.
        known.IntersectWith(item.Segments.Select(s => s.Id.ToString()));
        lock (state)
        {
            state.Message = saved == 0
                ? "No new shared audio found between these files"
                : $"Saved {saved} new segment(s). Check their type, then scan in Apply segments to find them in every file";
            if (known.Count > 0) state.Message += $" ({known.Count} already covered by saved segments)";
        }
    }

    /// <summary>
    /// Matches every file against the references and its named chapters. For each type a file has no saved segment
    /// of, the suggested matches are saved; all matches stay as the file's options in the dialog.
    /// </summary>
    private async Task ScanAsync(Guid itemId, ItemState state)
    {
        var item = await _mediaItemRepository.GetMediaItemFull(itemId)
                   ?? throw new InvalidOperationException("Media item not found");
        var targets = MediaFiles(item);

        var references = new Dictionary<SegmentType, (Fingerprinter Prints, Dictionary<Guid, SegmentInfo> ById)>();
        foreach (var type in SegmentTypes)
        {
            var segments = AudioReferences(item, type).ToList();
            var prints = await BuildReferencesAsync(segments);
            if (!prints.IsEmpty) references[type] = (prints, segments.ToDictionary(s => s.Id));
        }

        lock (state) state.Matches = new Dictionary<Guid, List<SegmentMatch>>();
        var segmentTypes = item.Segments.ToDictionary(s => s.Id, s => s.Type);
        var found = SegmentTypes.ToDictionary(t => t, _ => 0);
        var applied = 0;
        var failed = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            var file = targets[i];
            SetJob(state, FileLabel(item, file), i, targets.Count);
            try
            {
                var probe = await AudioExtractor.ProbeAsync(file.Path);
                var matches = new List<SegmentMatch>();
                foreach (var type in SegmentTypes)
                {
                    var spans = await FindInFileAsync(item.Id, file, probe, type, references.TryGetValue(type, out var r) ? r : null);
                    matches.AddRange(spans.Select(m => new SegmentMatch(m.SegmentId, type,
                        ToStoredStart(type, m.Span.Start, probe), m.Span.Length, m.FromChapter, m.Suggested)));
                    if (spans.Count > 0) found[type]++;

                    if (type == SegmentType.Intro) await _mediaFileRepository.UpdateMediaFileIntroProcessed(file.Id, true);
                    else await _mediaFileRepository.UpdateMediaFileOutroProcessed(file.Id, true);
                }
                lock (state) state.Matches[file.Id] = matches;

                // Saved segments are the user's (or an earlier scan's) choice; only types without one get the pick.
                var locations = file.SegmentLocations.Where(l => segmentTypes.ContainsKey(l.SegmentId)).ToList();
                var added = matches
                    .Where(m => m.Suggested && locations.All(l => segmentTypes[l.SegmentId] != m.Type))
                    .Select(m => new SegmentLocation
                        { SegmentId = m.SegmentId, FileId = file.Id, StartSeconds = m.Start, Length = m.Length })
                    .ToList();
                if (added.Count > 0)
                {
                    await _mediaItemRepository.ReplaceSegmentLocations(file.Id, [..locations, ..added]);
                    applied++;
                }
            }
            catch (Exception e)
            {
                failed++;
                _logger.LogError(e, "Segment scan failed for {file}", file.Path);
            }
        }

        lock (state)
        {
            state.Message = targets.Count == 0
                ? "No files to scan"
                : $"Scanned {targets.Count} file(s): intro found in {found[SegmentType.Intro]}, outro in {found[SegmentType.Outro]}"
                  + $"; saved new segments for {applied}"
                  + (failed > 0 ? $"; {failed} failed (see log)" : "");
        }
    }

    /// <summary>
    /// Everything found of one type in a file: its named chapter and every reference match. <c>Suggested</c> marks
    /// the pick the scan would make on its own (the named chapter, else the longest non-overlapping aligned matches).
    /// </summary>
    private async Task<List<(Guid SegmentId, Span Span, bool FromChapter, bool Suggested)>> FindInFileAsync(Guid itemId,
        MediaFile file, MediaProbe probe, SegmentType type, (Fingerprinter Prints, Dictionary<Guid, SegmentInfo> ById)? references)
    {
        var found = new List<(Guid SegmentId, Span Span, bool FromChapter, bool Suggested)>();
        var named = ChapterRules.Named(probe, type);
        if (named is not null) found.Add((await GetChapterReferenceAsync(itemId, type), named, true, true));

        // No references yet, or no audio: nothing to match until a reference is added.
        if (references is not { } refs || probe.AudioStream is null) return found;

        var region = type == SegmentType.Intro ? AudioRegion.Head : AudioRegion.Tail;
        var audio = await AudioExtractor.ExtractRegionAsync(file.Path, probe, region, TempFile());
        try
        {
            var matches = new List<(Guid SegmentId, Span Span)>();
            foreach (var match in await refs.Prints.QueryAsync(audio.Path))
            {
                var segmentId = Guid.Parse(match.TrackId);
                if (!refs.ById.TryGetValue(segmentId, out var reference)
                    || match.Length < reference.Length * MinReferenceCoverage) continue;
                var span = ChapterRules.Align(probe, ToSpan(audio, match), reference.Length);
                if (span.Length < ChapterRules.MinSegmentSeconds) continue;
                // The named chapter already is this segment.
                if (named is not null && span.Overlap(named) >= span.Length / 2) continue;
                matches.Add((segmentId, span));
            }

            // Several references can hit the same spot (two takes of an intro): all are offered, the longest is
            // suggested. Matches that fit no chapter of a chaptered file are likely false positives: not suggested.
            var preferred = ChapterRules.PreferAligned(probe, matches.Select(m => m.Span).ToList());
            foreach (var match in matches
                         .OrderByDescending(m => preferred.Contains(m.Span))
                         .ThenByDescending(m => m.Span.Length))
            {
                if (found.Any(f => f.SegmentId == match.SegmentId && f.Span.Overlap(match.Span) > 0)) continue;
                var suggested = named is null && preferred.Contains(match.Span)
                                && found.All(f => !f.Suggested || f.Span.Overlap(match.Span) <= 0);
                found.Add((match.SegmentId, match.Span, false, suggested));
            }
            return found;
        }
        finally
        {
            File.Delete(audio.Path);
        }
    }

    /// <summary>
    /// Locations taken from named chapters belong to a per item and type reference without audio
    /// (<see cref="SegmentInfo.FilePath"/> empty), not shown in the dialog and never matched against.
    /// </summary>
    private async Task<Guid> GetChapterReferenceAsync(Guid itemId, SegmentType type)
    {
        await _chapterReferenceLock.WaitAsync();
        try
        {
            var existing = (await _mediaItemRepository.GetMediaItemWithSegments(itemId))?.Segments
                .FirstOrDefault(s => IsChapterReference(s) && s.Type == type);
            if (existing is not null) return existing.Id;

            var reference = new SegmentInfo { Id = Guid.NewGuid(), ItemId = itemId, FilePath = "", Length = 0, Type = type };
            await _mediaItemRepository.AddSegmentInfo(itemId, reference);
            return reference.Id;
        }
        finally
        {
            _chapterReferenceLock.Release();
        }
    }

    private bool StartJob(Guid itemId, SegmentJobKind kind, Func<ItemState, Task> work)
    {
        var state = GetItemState(itemId);
        lock (state)
        {
            if (state.Job is not null) return false;
            state.Job = new SegmentJob(kind, "Starting", 0, 0);
            state.Message = null;
            state.Error = null;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await work(state);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Segment {kind} failed for item {itemId}", kind, itemId);
                lock (state) state.Error = e.Message;
            }
            finally
            {
                lock (state) state.Job = null;
            }
        });
        return true;
    }

    private async Task<Fingerprinter> BuildReferencesAsync(IEnumerable<SegmentInfo> segments)
    {
        var references = new Fingerprinter();
        foreach (var segment in segments)
        {
            var path = Path.Combine(WorkDirectory, segment.FilePath);
            if (!File.Exists(path))
            {
                _logger.LogWarning("Segment reference audio missing: {path}", path);
                continue;
            }
            await references.AddAsync(segment.Id.ToString(), path);
        }
        return references;
    }

    private ItemState GetItemState(Guid itemId) => _states.GetOrAdd(itemId, _ => new ItemState());

    private static void SetJob(ItemState state, string step, int done, int total)
    {
        lock (state) state.Job = state.Job! with { Step = step, Done = done, Total = total };
    }

    /// <summary>The item's video files in episode order (parsed season / episode, then path).</summary>
    private static List<MediaFile> MediaFiles(MediaItem item) => item.Files
        .Where(f => f.Type == FileType.File)
        .OrderBy(f => f.SeasonNumber ?? int.MaxValue)
        .ThenBy(f => f.EpisodeNumber ?? int.MaxValue)
        .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// Runs of consecutive files (in <paramref name="ordered"/>) that <paramref name="has"/>, labelled "S02–S04"
    /// for whole seasons, "S01E05–E09" / "S01E05–S04E03" otherwise, "All files" for everything without seasons.
    /// </summary>
    private static List<string> AppliedRanges(MediaItem item, List<MediaFile> ordered, Func<MediaFile, bool> has)
    {
        var ranges = new List<string>();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (!has(ordered[i])) continue;
            var from = i;
            while (i + 1 < ordered.Count && has(ordered[i + 1])) i++;
            ranges.Add(RangeLabel(item, ordered, from, i));
        }
        return ranges;
    }

    private static string RangeLabel(MediaItem item, List<MediaFile> ordered, int from, int to)
    {
        var first = ordered[from];
        var last = ordered[to];
        if (first.SeasonNumber is { } s1 && last.SeasonNumber is { } s2)
        {
            if (from == ordered.FindIndex(f => f.SeasonNumber == s1) && to == ordered.FindLastIndex(f => f.SeasonNumber == s2))
                return s1 == s2 ? $"S{s1:00}" : $"S{s1:00}–S{s2:00}";
            if (from == to) return FileLabel(item, first);
            var e1 = Numbers(first.Episodes).DefaultIfEmpty().Min();
            var e2 = Numbers(last.Episodes).DefaultIfEmpty().Max();
            return s1 == s2 ? $"S{s1:00}E{e1:00}–E{e2:00}" : $"S{s1:00}E{e1:00}–S{s2:00}E{e2:00}";
        }
        if (from == 0 && to == ordered.Count - 1 && ordered.Count > 1) return "All files";
        return from == to ? FileLabel(item, first) : $"{FileLabel(item, first)}–{FileLabel(item, last)}";
    }

    private static string RelativeTitle(MediaItem item, MediaFile file) =>
        Path.GetRelativePath(item.FolderDirectory, file.Path);

    /// <summary>"S01E04" (or "S01E04-E05", "E12") from the parsed tags, else the file's title without the item's name.</summary>
    private static string FileLabel(MediaItem item, MediaFile file)
    {
        var episodes = Numbers(file.Episodes);
        if (episodes.Count > 0)
        {
            var season = file.SeasonNumber is { } s ? $"S{s:00}" : "";
            var first = episodes.Min();
            var last = episodes.Max();
            return last == first ? $"{season}E{first:00}" : $"{season}E{first:00}-E{last:00}";
        }

        var title = file.GetDisplayTitle();
        var name = item.Title?.Trim() ?? "";
        if (name.Length > 0 && title.Length > name.Length && title.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            title = title[name.Length..].TrimStart(' ', '-', '.', '_', ':');
        return title;
    }

    private static List<int> Numbers(List<string>? values) =>
        values?.Select(v => int.TryParse(v, out var n) ? n : (int?)null).OfType<int>().ToList() ?? [];

    private static SegmentType TypeOf(AudioRegion region) => region == AudioRegion.Head ? SegmentType.Intro : SegmentType.Outro;

    /// <summary>As stored in <see cref="SegmentLocation"/>: intros from the start of the file, outros negative from its end.</summary>
    private static double ToStoredStart(SegmentType type, double start, MediaProbe probe) =>
        type == SegmentType.Intro ? start : start - probe.Duration;

    private static Span ToSpan(ExtractedAudio audio, AudioMatch match) =>
        new(audio.Start + match.QueryStart, audio.Start + match.QueryEnd);

    private static bool IsChapterReference(SegmentInfo segment) => string.IsNullOrEmpty(segment.FilePath);

    private static IEnumerable<SegmentInfo> AudioReferences(MediaItem item, SegmentType type) =>
        item.Segments.Where(s => s.Type == type && !IsChapterReference(s));

    private static void DeleteReferenceFiles(SegmentInfo segment)
    {
        if (IsChapterReference(segment)) return;
        var wav = Path.Combine(WorkDirectory, segment.FilePath);
        File.Delete(wav);
        File.Delete(Path.ChangeExtension(wav, PreviewExtension));
    }

    private static string TempFile() => Path.Combine(TempDirectory, $"{Guid.NewGuid()}.wav");

    /// <summary>A scan's find in a file, <see cref="Start"/> as in <see cref="SegmentLocation"/>.</summary>
    private record SegmentMatch(Guid SegmentId, SegmentType Type, double Start, double Length, bool FromChapter, bool Suggested);

    private class ItemState
    {
        public SegmentJob? Job;
        public string? Message;
        public string? Error;
        /// <summary>Last scan's finds per file id.</summary>
        public Dictionary<Guid, List<SegmentMatch>> Matches = new();
    }
}

public enum SegmentJobKind
{
    Detect,
    Scan
}

public record SegmentJob(SegmentJobKind Kind, string Step, int Done, int Total);

/// <param name="AppliedTo">Files that have it, as episode ranges ("S01E05–E09", "S02–S04"), see <c>AppliedRanges</c>.</param>
public record SegmentReference(Guid Id, SegmentType Type, double Length, List<string> AppliedTo);

/// <summary>
/// A segment a file has (<paramref name="Applied"/>, saved) and/or the last scan found in it (<paramref name="Found"/>).
/// <see cref="Start"/> as in <see cref="SegmentLocation"/>.
/// </summary>
/// <param name="FromChapter">Taken from a chapter named intro / outro instead of audio matching.</param>
public record SegmentOption(Guid SegmentId, SegmentType Type, double Start, double Length, bool FromChapter,
    bool Applied, bool Found);

/// <param name="Label">Short name (S01E04), <paramref name="Title"/> the path in the item.</param>
public record SegmentFileState(Guid Id, string Label, string Title, bool IntroScanned, bool OutroScanned,
    List<SegmentOption> Segments);

/// <summary>A segment to save for a file, see <see cref="SegmentFinderService.ApplyAsync"/>.</summary>
public record SegmentPick(Guid SegmentId, double Start, double Length);

public record SegmentsState(Guid ItemId, SegmentJob? Job, string? Message, string? Error,
    List<SegmentReference> References, List<SegmentFileState> Files);
