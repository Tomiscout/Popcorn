import { filenameParse } from '@ctrl/video-filename-parser';

export function parseFilenames(files: Record<string, string>): Record<string, any> {
  const result: Record<string, any> = {};

  for (const [id, filename] of Object.entries(files)) {
    let tags;
    try {
      tags = filenameParse(filename, false);
    } catch {
      tags = filenameParse(filename, true);
    }
    result[id] = tags;
  }

  return result;
}
