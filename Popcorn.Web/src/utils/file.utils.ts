export const subtitleFormats = ['.vtt', '.srt', '.ass', '.ssa', '.sup'];
export const audioFormats = ['.aac', '.mp3', '.opus', '.ogg', '.flac', '.wav'];

function hasExtension(fileName: string, extensions: readonly string[]): boolean {
  const lower = fileName.toLowerCase();
  return extensions.some((ext) => lower.endsWith(ext));
}

export function isSubtitleFormat(fileName: string): boolean {
  return hasExtension(fileName, subtitleFormats);
}

export function isAudioFormat(fileName: string): boolean {
  return hasExtension(fileName, audioFormats);
}
