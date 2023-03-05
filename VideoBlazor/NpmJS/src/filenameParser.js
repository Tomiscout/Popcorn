import { filenameParse } from '@ctrl/video-filename-parser';

export function parseFilename(filename, isTv = false) {
    return filenameParse(filename, isTv);
}