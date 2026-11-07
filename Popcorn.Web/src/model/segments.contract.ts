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
  segments: SegmentOption[];
}

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
    description: 'Every intro and outro is skipped after a short count-down.',
  },
  {
    value: 'AfterFirstOccurrence',
    label: "Don't auto-skip a segment's first occurrence",
    description: 'Each different intro/outro (e.g. a new season opening) is shown once, then skipped.',
  },
  { value: 'Never', label: "Don't auto-skip", description: 'Only the skip button is shown.' },
];

/** Server `PlayerSegments` (web player metadata; the native player gets the same in PascalCase). */
export interface PlayerSegmentsDto {
  itemId: string | null;
  globalMode: SegmentAutoSkip;
  itemMode: SegmentAutoSkip | null;
  /** Skipping an outro that ends near the end of the file skips the rest of the file too. */
  skipOutroTail: boolean;
  /** Auto-skip at once instead of after the skip button's count-down. */
  skipSegmentsWithoutDelay: boolean;
  segments: {
    segmentId: string;
    /** Seconds; negative = outro, relative to the end of the file. */
    startSeconds: number;
    length: number;
    seen: boolean;
  }[];
}
