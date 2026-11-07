export function convertSrtToVtt(data: string): string {
  // Remove DOS newlines
  const srt = data.replace(/\r+/g, '').trim();

  const cues = srt.split('\n\n');
  let result = '';

  if (cues.length > 0) {
    result += 'WEBVTT\n\n';

    for (const cue of cues) {
      result += convertSrtCue(cue);
    }
  }

  return result;
}

function convertSrtCue(cueLine: string) {
  let cue = '';
  const s = cueLine.split(/\n/);

  // Concatenate multi-line strings into one
  while (s.length > 3) {
    for (let i = 3; i < s.length; i++) {
      s[2] += '\n' + s[i];
    }
    s.splice(3, s.length - 3);
  }

  let line = 0;

  // Detect identifier
  if (!s[0].match(/\d+:\d+:\d+/) && s[1].match(/\d+:\d+:\d+/)) {
    cue += (s[0].match(/\w+/) || [''])[0] + '\n';
    line += 1;
  }

  // Get time strings
  if (s[line].match(/\d+:\d+:\d+/)) {
    const m = s[1].match(/(\d+):(\d+):(\d+)(?:,(\d+))?\s*--?>\s*(\d+):(\d+):(\d+)(?:,(\d+))?/);

    if (m) {
      cue += `${m[1]}:${m[2]}:${m[3]}.${m[4]} --> ` + `${m[5]}:${m[6]}:${m[7]}.${m[8]}\n`;
      line += 1;
    } else {
      // Unrecognized time string
      return '';
    }
  } else {
    // File format error or comment lines
    return '';
  }

  // Get cue text
  if (s[line]) {
    cue += sanitizeForWebVTT(s[line]) + '\n\n';
  }

  return cue;
}

function sanitizeForWebVTT(text: string): string {
  return (
    text
      // remove <font> and its attributes
      .replace(/<\/?font[^>]*>/gi, '')
      // allow only WebVTT-supported tags (<b>, <i>, <u>, <c>, <v>, <lang>)
      .replace(/<(?!\/?(b|i|u|c|v|lang)(\s+[^>]*)?>)/gi, '')
      .trim()
  );
}
