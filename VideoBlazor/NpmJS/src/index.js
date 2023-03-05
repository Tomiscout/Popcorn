import { parseFilename } from './filenameParser';

export function ParseFilename(filename, isTv = false) {
    return parseFilename(filename, isTv);
}