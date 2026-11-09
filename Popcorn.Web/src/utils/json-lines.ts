/**
 * Reads a response that sends one JSON object per line while it stays open (server `Push/JsonLines.cs`: download
 * ranges, audio track conversion, live logs), calling `onLine` for each. Resolves when the server ends it, `false`
 * right away for a 404; rejects on other errors and with an `AbortError` when `init.signal` aborts.
 */
export async function readJsonLines<T>(
  url: string,
  init: RequestInit,
  onLine: (value: T) => void
): Promise<boolean> {
  const response = await fetch(url, init);
  if (response.status === 404) return false;
  if (!response.ok || !response.body) throw new Error(`${url}: ${response.status}`);

  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
  let buffered = '';
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffered += value;
    let newline: number;
    while ((newline = buffered.indexOf('\n')) >= 0) {
      const line = buffered.slice(0, newline).trim();
      buffered = buffered.slice(newline + 1);
      if (line) onLine(JSON.parse(line) as T);
    }
  }
  return true;
}

export const isAbortError = (error: unknown) => error instanceof DOMException && error.name === 'AbortError';
