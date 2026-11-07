/**
 * Transport to the native player process (Popcorn.Player.Core `WebEventHandler`): a WebSocket to its Fleck
 * server, address injected as `window.wsAddress` by the native layer (CEF on Linux, WebView2 on Windows).
 * Messages are JSON `{ name, data }` both ways; binary messages are seek-bar thumbnails (JPEG) for the last
 * `thumb_requested`.
 *
 * Development (`npm run dev:player`): `player.html?ws=ws://127.0.0.1:<port>` against a running player
 * (its address is in the player log).
 */

export interface NativeEvent {
  name: string;
  data: any;
}

export interface NativeBridgeHandlers {
  onEvent: (event: NativeEvent) => void;
  onThumbnail: (blobUrl: string) => void;
}

export interface NativeBridge {
  send: (name: string, data?: unknown) => void;
  close: () => void;
}

declare global {
  interface Window {
    wsAddress?: string;
    /** Set by the Windows player: dropping files on the window, the host's subtitle file picker. */
    playerHost?: 'windows';
    /** WebView2 (Windows player): hands the host dropped files, whose paths a page can't read. */
    chrome?: { webview?: { postMessageWithAdditionalObjects: (message: unknown, objects: FileList) => void } };
  }
}

const socketAddress = () => window.wsAddress ?? new URLSearchParams(window.location.search).get('ws');

export function connectNativeBridge({ onEvent, onThumbnail }: NativeBridgeHandlers): NativeBridge {
  const address = socketAddress();
  if (!address) {
    throw new Error('No native player bridge: window.wsAddress or ?ws= missing');
  }

  const socket = new WebSocket(address);
  const pending: string[] = [];
  const sendRaw = (message: string) => {
    if (socket.readyState === WebSocket.OPEN) socket.send(message);
    else pending.push(message);
  };

  socket.onopen = () => {
    // `initialized` releases the messages C# queued before the page was up.
    socket.send(JSON.stringify({ name: 'initialized', data: null }));
    for (const message of pending.splice(0)) socket.send(message);
  };
  socket.onerror = (error) => console.error('Native bridge error', error);
  socket.onclose = () => console.log('Native bridge closed');
  socket.onmessage = (event) => {
    if (event.data instanceof Blob) {
      onThumbnail(URL.createObjectURL(event.data));
      return;
    }
    try {
      onEvent(JSON.parse(event.data));
    } catch (error) {
      console.error('Bad native bridge message', event.data, error);
    }
  };

  return {
    send: (name, data = null) => sendRaw(JSON.stringify({ name, data })),
    close: () => socket.close(),
  };
}
