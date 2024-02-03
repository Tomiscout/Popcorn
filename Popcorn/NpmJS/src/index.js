import { parseFilename } from './filenameParser';

export function ParseFilename(filenames) {
    const result = [];
    
    for (const filename of filenames) {
        try {
            var tags = parseFilename(filename, false);
        } catch (e) {
            tags = parseFilename(filename, true);
        }
        result.push(tags);
    }
    return result;
}
