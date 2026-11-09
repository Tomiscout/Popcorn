import { useAppStore } from '../state/app.store';
import type { SocketPayloads } from './socket-payloads';
import type { SocketRequests } from './socket-requests';

/** Payload of an event: the generated server type (`socket-payloads.ts`), none for events without a handler payload. */
export type PayloadOf<K extends SocketRequests> = K extends keyof SocketPayloads ? SocketPayloads[K] : never;

type QueuedMessage = { kind: 'text'; data: string } | { kind: 'binary'; data: Blob };

export class WebSocketClient {
  private ws?: WebSocket;
  private eventHandlers: Map<string, Set<(payload: any) => void>> = new Map();
  private blobEventHandlers: Map<string, Set<(payload: Uint8Array<ArrayBuffer>, metadata: string[]) => void>> =
    new Map();

  private readyListeners: Set<(ready: boolean) => void> = new Set();

  public isReady: boolean = false;

  private url: string;
  private reconnectAttempts = 0;
  private reconnectTimer?: number;
  private manuallyClosed = false;
  private sendQueue: QueuedMessage[] = [];
  private connectTimeout?: number;

  constructor(url: string) {
    this.url = url;
    this.connect();
  }

  //#region Connection Management
  private connect() {
    console.log('Connecting websocket', this.url);
    useAppStore.getState().setWebSocketConnecting(true);
    this.ws = new WebSocket(this.url);

    this.connectTimeout = window.setTimeout(() => {
      if (this.ws?.readyState === WebSocket.CONNECTING) {
        console.warn('WS connect timeout, forcing reconnect');
        this.ws.close();
      }
    }, 5000);

    this.ws.onopen = () => {
      clearTimeout(this.connectTimeout);
      useAppStore.getState().setWebSocketConnecting(false);
      useAppStore.getState().setWebSocketReconnecting(false);

      console.log('Websocket connected');
      this.isReady = true;
      this.reconnectAttempts = 0;
      this.notifyReady(true);
      this.flushQueue();
    };

    this.ws.onclose = () => {
      clearTimeout(this.connectTimeout);
      this.isReady = false;
      this.notifyReady(false);
      if (!this.manuallyClosed) this.scheduleReconnect();
    };

    this.ws.onerror = () => {
      this.ws?.close();
    };

    this.ws.onmessage = this.handleMessage.bind(this);
  }

  private scheduleReconnect() {
    useAppStore.getState().setWebSocketConnecting(true);
    useAppStore.getState().setWebSocketReconnecting(true);

    const delay = Math.min(1000 * 2 ** this.reconnectAttempts, 30_000);
    this.reconnectAttempts++;

    console.log('Reconnecting in', delay, 'ms');
    clearTimeout(this.reconnectTimer);
    this.reconnectTimer = window.setTimeout(() => this.connect(), delay);
  }

  private flushQueue() {
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;

    for (const msg of this.sendQueue) {
      if (msg.kind === 'text') this.ws.send(msg.data);
      else this.ws.send(msg.data);
    }

    this.sendQueue = [];
  }
  //#endregion

  //#region Event sending
  sendEvent<K extends SocketRequests>(
    type: K,
    ...[payload = {} as PayloadOf<K>]: PayloadOf<K> extends void ? [] : [payload: PayloadOf<K>]
  ) {
    console.debug('Sending event', type);
    const message = JSON.stringify({ type, payload });

    if (this.ws?.readyState === WebSocket.OPEN) {
      this.ws.send(message);
    } else {
      this.sendQueue.push({ kind: 'text', data: message });
    }
  }

  sendBinaryEvent(type: string, data: Blob | ArrayBuffer | Uint8Array, ...params: string[]) {
    console.log('Sending binary event', type);
    const header = `${type},${params.join(',')};`;
    const headerBytes = new TextEncoder().encode(header);

    const payloadBlob = new Blob([headerBytes, data as any]);

    if (this.ws?.readyState === WebSocket.OPEN) {
      this.ws.send(payloadBlob);
    } else {
      this.sendQueue.push({ kind: 'binary', data: payloadBlob });
    }
  }
  //#endregion

  //#region Event handling
  handleMessage(event: MessageEvent) {
    if (typeof event.data === 'string') {
      const { type, payload } = JSON.parse(event.data);
      console.debug('Got event', type);

      const handlers = this.eventHandlers.get(type);
      handlers?.forEach((h) => h(payload));
    } else if (event.data instanceof Blob) {
      event.data.arrayBuffer().then((buffer) => {
        const bytes = new Uint8Array(buffer);
        let separatorIndex = -1;

        for (let i = 0; i < bytes.length; i++) {
          if (bytes[i] === 59) {
            // ASCII code for ';'
            separatorIndex = i;
            break;
          }
        }

        if (separatorIndex === -1) return; // malformed message

        const decoder = new TextDecoder('utf-8');
        const metadata = decoder.decode(bytes.slice(0, separatorIndex)).split(',');
        const payload = bytes.slice(separatorIndex + 1);

        const handlers = this.blobEventHandlers.get(metadata[0]);
        handlers?.forEach((h) => h(payload, metadata.slice(1)));
      });
    }
  }

  onEvent<TPayload>(type: string, handler: (payload: TPayload) => void) {
    if (!this.eventHandlers.has(type)) {
      this.eventHandlers.set(type, new Set());
    }

    this.eventHandlers.get(type)!.add(handler as any);

    // ✅ return unsubscribe (important)
    return () => {
      this.eventHandlers.get(type)?.delete(handler as any);
    };
  }

  onBlobEvent(type: string, handler: (payload: Uint8Array<ArrayBuffer>, metadata: string[]) => void) {
    if (!this.blobEventHandlers.has(type)) {
      this.blobEventHandlers.set(type, new Set());
    }

    this.blobEventHandlers.get(type)!.add(handler);

    return () => {
      this.blobEventHandlers.get(type)?.delete(handler);
    };
  }

  //#endregion

  close() {
    this.ws?.close();
  }

  private notifyReady(ready: boolean) {
    this.readyListeners.forEach((cb) => cb(ready));
  }

  onReadyChange(cb: (ready: boolean) => void) {
    this.readyListeners.add(cb);
  }

  onReadyChangeDelete(cb: (ready: boolean) => void) {
    this.readyListeners.delete(cb);
  }
}
