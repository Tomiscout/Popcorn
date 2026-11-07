import { usePlayerStore } from '../state/player.store';
import { postMessage } from '../utils/events';
import type { NextFilePlan } from '../query/models/video.models';

/** A host got the plan for what comes next (end of file, next button, refresh): play it or warn about the gap. */
export function applyNextFilePlan(plan: NextFilePlan) {
  const { setNextFileWarning, isPlaying, playbackEnded } = usePlayerStore.getState();
  if (plan.missingLabel) {
    setNextFileWarning(plan);
    if (isPlaying) postMessage('play_status_changed', false);
    return;
  }
  setNextFileWarning(null);
  if (plan.nextFileId) postMessage('play_file', plan.nextFileId);
  else if (playbackEnded) usePlayerStore.setState({ showFin: true, isPlaying: false });
}

function fileName(label?: string | null, episodeTitle?: string | null, title?: string | null): string | null {
  const quoted = episodeTitle ? `“${episodeTitle}”` : null;
  const parts = label ? [label, quoted] : [quoted ?? title];
  return parts.filter(Boolean).join(' · ') || null;
}

/** Next button tooltip: "S01E03 · “Episode name”" (no item title), else the file's own name; null = nothing next. */
export function nextFileName(plan: NextFilePlan | null | undefined): string | null {
  if (!plan?.nextFileId) return null;
  return fileName(plan.nextLabel, plan.nextEpisodeTitle, plan.nextTitle);
}

/** Previous button tooltip, same format; null = nothing before this file. */
export function previousFileName(plan: NextFilePlan | null | undefined): string | null {
  if (!plan?.previousFileId) return null;
  return fileName(plan.previousLabel, plan.previousEpisodeTitle, plan.previousTitle);
}
