/**
 * Transport to the native player process (Popcorn.Player.Core `WebEventHandler`). Messages are JSON
 * `{ name, data }` both ways; binary messages are seek-bar thumbnails (JPEG) for the last `thumb_requested`.
 *
 * - Linux (Popcorn.Player.Linux): WebSocket to the C# Fleck server, address injected as `window.wsAddress`.
 * - Windows (Popcorn.Player.Windows): WebView2 `chrome.webview` messages + shared buffers.
 * - Development (`npm run dev:player`): `player.html?ws=ws://127.0.0.1:<port>` against a running player
 *   (its address is in the player log).
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
  /** Linux leaves the subtitle file picker to the page (none in CEF OSR); Windows opens its own. */
  readonly kind: 'websocket' | 'webview';
  send: (name: string, data?: unknown) => void;
  close: () => void;
}

interface WebView2 {
  postMessage: (message: unknown) => void;
  addEventListener: (type: string, listener: (event: any) => void) => void;
  removeEventListener: (type: string, listener: (event: any) => void) => void;
}

declare global {
  interface Window {
    wsAddress?: string;
    chrome?: { webview?: WebView2 };
  }
}

const socketAddress = () => window.wsAddress ?? new URLSearchParams(window.location.search).get('ws');

export function connectNativeBridge({ onEvent, onThumbnail }: NativeBridgeHandlers): NativeBridge {
  const webview = window.chrome?.webview;
  if (webview) {
    const onMessage = (e: { data: NativeEvent }) => onEvent(e.data);
    const onSharedBuffer = (e: any) => {
      const blob = new Blob([e.getBuffer()], { type: 'image/jpeg' });
      onThumbnail(URL.createObjectURL(blob));
    };
    webview.addEventListener('message', onMessage);
    webview.addEventListener('sharedbufferreceived', onSharedBuffer);
    webview.postMessage({ name: 'initialized', data: null });

    return {
      kind: 'webview',
      send: (name, data = null) => webview.postMessage({ name, data }),
      close: () => {
        webview.removeEventListener('message', onMessage);
        webview.removeEventListener('sharedbufferreceived', onSharedBuffer);
      },
    };
  }

  const address = socketAddress();
  if (!address) {
    throw new Error('No native player bridge: window.wsAddress, chrome.webview or ?ws= missing');
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
    kind: 'websocket',
    send: (name, data = null) => sendRaw(JSON.stringify({ name, data })),
    close: () => socket.close(),
  };
}
