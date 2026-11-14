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
  /** Socket lost (`false`) or back again after a loss (`true`); not called for the first connect. */
  onConnectionChange?: (connected: boolean) => void;
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

const RETRY_MS = 500;

const socketAddress = () => window.wsAddress ?? new URLSearchParams(window.location.search).get('ws');

export function connectNativeBridge({ onEvent, onThumbnail, onConnectionChange }: NativeBridgeHandlers): NativeBridge {
  const address = socketAddress();
  if (!address) {
    throw new Error('No native player bridge: window.wsAddress or ?ws= missing');
  }

  // The socket can drop while the player keeps running (seen after a system sleep/resume): retry every
  // RETRY_MS until the page is closed (the player is local, no backoff needed). Every (re)connect sends `initialized`; C# answers a reconnect with a state replay
  // (PlayerCore.ResendState). Messages sent meanwhile wait in `pending`.
  const pending: string[] = [];
  let socket: WebSocket;
  let closed = false;
  let lost = false;
  let retryTimer: ReturnType<typeof setTimeout> | undefined;

  const connect = () => {
    socket = new WebSocket(address);
    socket.onopen = () => {
      socket.send(JSON.stringify({ name: 'initialized', data: null }));
      for (const message of pending.splice(0)) socket.send(message);
      if (lost) {
        lost = false;
        console.log('Native bridge reconnected');
        onConnectionChange?.(true);
      }
    };
    socket.onerror = (error) => console.error('Native bridge error', error);
    socket.onclose = (event) => {
      if (closed) return;
      if (!lost) {
        lost = true;
        console.warn(`Native bridge closed (${event.code}), reconnecting every ${RETRY_MS} ms`);
        onConnectionChange?.(false);
      }
      retryTimer = setTimeout(connect, RETRY_MS);
    };
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
  };
  connect();

  const sendRaw = (message: string) => {
    if (socket.readyState === WebSocket.OPEN) socket.send(message);
    else pending.push(message);
  };

  return {
    send: (name, data = null) => sendRaw(JSON.stringify({ name, data })),
    close: () => {
      closed = true;
      clearTimeout(retryTimer);
      socket.close();
    },
  };
}
