export class WebSocketClient {
  private ws: WebSocket;
  private eventHandlers: Map<string, (payload: any) => void>;
  private blobEventHandlers: Map<string, (payload: Uint8Array<ArrayBuffer>, metadata: string[]) => void>;
  private readyListeners: Set<(ready: boolean) => void> = new Set();

  public isReady: boolean = false;

  constructor(url: string) {
    console.log('Creating new websocket', url);
    this.ws = new WebSocket(url);
    console.log('Websocket created', url);
    this.eventHandlers = new Map();
    this.blobEventHandlers = new Map();

    this.ws.onopen = () => {
      this.isReady = true;
      this.notifyReady(true);
    };

    this.ws.onclose = () => {
      this.isReady = false;
      this.notifyReady(false);
    };

    this.ws.onmessage = this.handleMessage.bind(this);
  }

  sendEvent<TPayload>(type: string, payload: TPayload) {
    console.log('Sending event', type);
    const message = JSON.stringify({ type, payload });
    if (this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(message);
    } else {
      this.ws.addEventListener('open', () => this.ws.send(message), {
        once: true,
      });
    }
  }

  handleMessage(event: MessageEvent) {
    if (typeof event.data === 'string') {
      const { type, payload } = JSON.parse(event.data);
      console.log('Got event', type);

      const handler = this.eventHandlers.get(type);
      if (handler) handler(payload);
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

        const handler = this.blobEventHandlers.get(metadata[0]);
        if (handler) handler(payload, metadata.slice(1));
      });
    }
  }

  onEvent<TPayload>(type: string, handler: (payload: TPayload) => void) {
    this.eventHandlers.set(type, handler);
  }

  onBlobEvent(type: string, handler: (payload: Uint8Array<ArrayBuffer>, metadata: string[]) => void) {
    this.blobEventHandlers.set(type, handler);
  }

  onEventUnregiser(type: string) {
    this.eventHandlers.delete(type);
  }

  onBlobEventUnregiser(type: string) {
    this.blobEventHandlers.delete(type);
  }

  close() {
    this.ws.close();
  }

  private notifyReady(ready: boolean) {
    this.readyListeners.forEach((cb) => cb(ready));
  }

  onReadyChange(cb: (ready: boolean) => void) {
    this.readyListeners.add(cb);
  }

  offReadyChange(cb: (ready: boolean) => void) {
    this.readyListeners.delete(cb);
  }
}
