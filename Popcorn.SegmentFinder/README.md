# Popcorn.SegmentFinder

Intro/outro detection (SoundFingerprinting + FFMpegCore), used by `Popcorn.Server`. How the players skip the found
segments: [Features](../Docs/Features.md#introoutro-skipping).

## Pieces

- `SegmentFinderService`: singleton, background jobs, in-memory candidates.
- Server side: `SegmentsWebService` (`Segments*` socket events; the dialog subscribes to `SegmentsState`) +
  `SegmentsController` (`api/segments/{id}/audio` Opus preview).
- UI: `pages/item-details/segments-dialog.tsx`. Tabs Segment candidates = detect + saved segments with
  type/delete/applied-to ranges; Apply segments = scan + per-file toggles. Files labelled S01E04 from parsed tags,
  full path on hover.

## Detect

Heads/tails of 2–5 files (`MaxDetectFiles`), each fingerprinted against the ones before it. Audio shared by any two
is saved right away as `SegmentInfo` references (type guessed: head = intro, tail = outro; `SegmentsSetType` changes
it and drops its locations), wav in `<data>/segmentFinder`.

## Scan

Every file is matched against the references. For each type a file has no saved location of, the scan's pick
(`Suggested`) is saved as `SegmentLocation` (outro start negative = from end). All matches stay in memory as per-file
alternatives that `SegmentsApply` (one click per segment, `ReplaceSegmentLocations`) switches to.

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
