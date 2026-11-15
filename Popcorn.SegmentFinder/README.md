# Popcorn.SegmentFinder

Intro/outro detection (SoundFingerprinting + FFMpegCore), used by `Popcorn.Server`. How the players skip the found
segments: [Features](../Docs/Features.md#introoutro-skipping).

## Pieces

- `SegmentFinderService`: singleton, background jobs, in-memory candidates.
- Server side: `SegmentsWebService` (`Segments*` socket events; the dialog subscribes to `SegmentsState`) +
  `SegmentsController` (`api/segments/{id}/audio` Opus preview; `api/segments/{id}/edit/{waveform,audio}?itemId=`
  for the range editor).
- UI: `pages/item-details/segments-dialog.tsx`. Tabs Segment candidates = detect + saved segments with
  type/edit/delete/applied-to ranges (edit = `segment-range-editor.tsx`, see below); Apply segments = the same saved segments list with ticks + scans + per-file toggles. Files labelled S01E04 from parsed tags,
  full path on hover.

## Silence and timing (`AudioEnvelope`)

Silence has no fingerprints, so a segment with silence in it (credits, silence, a logo sting, silence to the end of
the file — a very common ending) only matches in pieces, each shorter than the 5 s minimum. And fingerprint match
times are off by up to ~3 s. `AudioEnvelope` (RMS loudness of the fingerprint wav in ~10 ms frames, silence
≤ -50 dBFS) covers both:

- `Fingerprinter.QueryAsync` moves every match to where the loudness of the matched part correlates best (±5 s of
  the fingerprints' guess; kept when nothing correlates clearly, e.g. dialogue over the song).
- Detect grows shared matches over the silence both files have next to them before merging and the 5 s check.
- Scan counts the reference's silence that the file shares at the same spot as matched (coverage), takes the whole
  reference where it lines up and adds the file's silence around it. A span with only silence up to the region's
  edge runs to the edge (an outro then ends at the end of the file).
- Overlapping or touching matches of other references grow the scan's pick (credits music + sting = one outro).

A reference that is all silence still cannot be found (nothing anchors it).

## Detect

Heads/tails of 2–5 files (`MaxDetectFiles`), each fingerprinted against the ones before it. Audio shared by any two
is saved right away as `SegmentInfo` references (type guessed: head = intro, tail = outro; `SegmentsSetType` changes
it and drops its locations), wav in `<data>/segmentFinder`.

Each reference stores where it was cut from (`source_file_id`, `source_start`; null for references saved before
that, migration `SegmentSource_20261115`).

## Edit range

The pencil of a saved segment opens the range editor: `SegmentsEditInfo` answers with the window it works on, the
source file from `EditMarginSeconds` (30 s) before to 30 s after the reference, or only the reference's own Opus clip
when the source is unknown or gone (then it can only be shortened). The waveform comes from `AudioWaveform` (shared
canvas helpers with the player's subtitle syncer in `src/utils/waveform.ts`), the listening clip of the window is made
on first request in `segmentFinder/tmp`. `SegmentsSetRange` re-cuts the wav + Opus clip, stores the new length /
source start and drops all locations of the reference (`SetSegmentRange`), so its files get scanned again; the dialog
confirms that first when the reference is applied anywhere.

## Scan

Every file is matched against the references. For each type a file has no saved location of, the scan's pick
(`Suggested`) is saved as `SegmentLocation` (outro start negative = from end). All matches stay in memory as per-file
alternatives that `SegmentsApply` (one click per segment, `ReplaceSegmentLocations`) switches to.

`SegmentsScan` (`SegmentScanOptions`) runs three ways from the dialog:

- Scan all: every file, every reference + named chapters (the above).
- Scan missing: the files missing a segment of a type in use, only the ticked references.
- Scan selected: the ticked files and references; the pick *replaces* the file's saved locations of that type
  (`Replace`), e.g. one of two outros put on two episodes.

With a subset of references (`SegmentIds` not null) only their types are scanned, chapters are not read (a named
chapter would win over the picked segment) and `*_processed` is not set; the files' earlier matches of other
references stay. The dialog sends `SegmentIds: null` when every reference is ticked.

Files not fully downloaded (`IIncompleteFiles`, implemented by `DownloadManager`: any file of a download with
progress < 1, also paused or skipped parts) are never decoded: their missing parts read as zeros and ffmpeg logs an
error per packet. Scans skip them (stay "not scanned", the message counts them), detection refuses them, the dialog
shows them as "downloading" and unselectable; `LivePushService` re-pushes open dialogs' state on download changes,
so they become selectable when they finish. ffmpeg failures are logged as one line (exit code + up to 5 distinct
error lines, `AudioExtractionException`), not its whole output.

- `intro_processed`/`outro_processed` only mean "scanned" (UI "not scanned"). A new/deleted reference resets them for
  files without a location of that type.
- References report `AppliedTo` episode ranges ("S02–S04", "S01E05–S04E03").
- Deleting a media item drops its reference audio + candidates (`ForgetItem`).

## File chapters (`ChapterRules`)

- A chapter titled as intro/outro (`TypeOf`: intro/opening/OP/theme song…, outro/ending/ED/credits…) is taken as the
  segment as is. It is stored under a per-item audio-less `SegmentInfo` with empty `file_path`, hidden from the
  dialog. Scans read chapters even with no references.
- Other chapters snap matches/candidates to chapter boundaries: a chapter of similar length to the match or reference
  (±25 %) replaces it, whether the match is shorter or longer. Unaligned matches are dropped.
- Segments shorter than `ChapterRules.MinSegmentSeconds` (5 s) are always dropped.
