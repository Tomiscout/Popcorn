/** Mirrors Popcorn.SegmentFinder (SegmentFinderService state records). Times in seconds. */

export enum SegmentType {
  Intro = 0,
  Outro = 1,
}

export enum SegmentJobKind {
  Detect = 0,
  Scan = 1,
}

export interface SegmentJob {
  kind: SegmentJobKind;
  step: string;
  done: number;
  total: number;
}

/** Saved intro/outro audio that files are scanned for. */
export interface SegmentReference {
  id: string;
  type: SegmentType;
  length: number;
  /** Files that have it, as episode ranges ("S01E05–E09", "S02–S04"). */
  appliedTo: string[];
  /** Changes when its audio is re-cut (cache key of the listening clip). */
  audioVersion: number;
}

/**
 * Range editor of a saved segment (answer to `SegmentsEditInfo`), seconds in the audio it is cut from: the source
 * media file (`sourceLabel`) or, when that is unknown, the segment's own clip (`sourceLabel` null, only trimming).
 * The waveform / audio endpoints `api/segments/{id}/edit/{waveform|audio}` cover `windowStart` + `windowLength`.
 */
export interface SegmentEditInfo {
  segmentId: string;
  type: SegmentType;
  sourceLabel: string | null;
  /** Where the segment starts now. */
  start: number;
  length: number;
  windowStart: number;
  windowLength: number;
}

export interface SegmentSpan {
  start: number;
  length: number;
}

/** A segment the file has (`applied`, saved) and/or the last scan found in it (`found`). */
export interface SegmentOption extends SegmentSpan {
  segmentId: string;
  type: SegmentType;
  /** Taken from a chapter named intro / outro instead of audio matching. */
  fromChapter: boolean;
  applied: boolean;
  found: boolean;
}

export interface SegmentFileState {
  id: string;
  /** Short name (S01E04); `title` is the path in the item. */
  label: string;
  title: string;
  introScanned: boolean;
  outroScanned: boolean;
  /** Not fully downloaded: scans skip it, detection refuses it. */
  downloading: boolean;
  segments: SegmentOption[];
}

/** The file has a segment of this type applied (not only found). */
export const hasApplied = (f: SegmentFileState, type: SegmentType) =>
  f.segments.some((o) => o.type === type && o.applied);

export interface SegmentsState {
  itemId: string;
  job: SegmentJob | null;
  message: string | null;
  error: string | null;
  references: SegmentReference[];
  files: SegmentFileState[];
}

export interface SegmentsDetectRequest {
  itemId: string;
  fileIds: string[];
}

/** Changes a saved segment's type (its file locations are dropped, scan again). */
export interface SegmentsSetTypeRequest {
  itemId: string;
  segmentId: string;
  type: SegmentType;
}

export interface SegmentsDeleteRequest {
  itemId: string;
  segmentId: string;
}

export interface SegmentPick {
  segmentId: string;
  start: number;
  length: number;
}

/** Per file id the segments it should have (replacing its saved ones); files left out are not touched. */
export interface SegmentsApplyRequest {
  itemId: string;
  files: Record<string, SegmentPick[]>;
}

/** When players skip intros/outros on their own (`SegmentAutoSkip`): global setting, per-item override. */
export type SegmentAutoSkip = 'Always' | 'AfterFirstOccurrence' | 'Never';

export const segmentAutoSkipOptions: { value: SegmentAutoSkip; label: string; description: string }[] = [
  {
    value: 'Always',
    label: 'Auto-skip all intros/outros',
    description: 'Every intro and outro is skipped after the auto-skip delay.',
  },
  {
    value: 'AfterFirstOccurrence',
    label: "Don't auto-skip a segment's first occurrence",
    description: 'Each different intro/outro (e.g. a new season opening) is shown once, then skipped.',
  },
  { value: 'Never', label: "Don't auto-skip", description: 'Only the skip button is shown.' },
];

/** Bounds of `SettingsModel.SegmentAutoSkipDelay` (seconds). */
export const MAX_AUTO_SKIP_DELAY = 99999;
export const DEFAULT_AUTO_SKIP_DELAY = 10;

/** Whole seconds within 0..MAX_AUTO_SKIP_DELAY. */
export const clampAutoSkipDelay = (value: number) =>
  Math.min(MAX_AUTO_SKIP_DELAY, Math.max(0, Math.round(Number.isFinite(value) ? value : DEFAULT_AUTO_SKIP_DELAY)));

/** Server `PlayerSegments` (web player metadata; the native player gets the same in PascalCase). */
export interface PlayerSegmentsDto {
  itemId: string | null;
  globalMode: SegmentAutoSkip;
  itemMode: SegmentAutoSkip | null;
  /** Skipping an outro that ends near the end of the file skips the rest of the file too. */
  skipOutroTail: boolean;
  /** Seconds of the skip button's count-down before auto-skip (0 = at once); global setting. */
  autoSkipDelay: number;
  segments: {
    segmentId: string;
    /** Seconds; negative = outro, relative to the end of the file. */
    startSeconds: number;
    length: number;
    seen: boolean;
  }[];
}
