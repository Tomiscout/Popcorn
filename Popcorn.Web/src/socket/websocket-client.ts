export class WebSocketClient {
  private ws: WebSocket;
  private eventHandlers: Map<string, (payload: any) => void>;

  constructor(url: string) {
    this.ws = new WebSocket(url);
    this.eventHandlers = new Map();

    this.ws.onmessage = this.handleMessage.bind(this);
  }

  sendEvent<TPayload>(type: string, payload: TPayload) {
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
    const { type, payload } = JSON.parse(event.data);
    const handler = this.eventHandlers.get(type);
    if (handler) handler(payload);
  }

  onEvent<TPayload>(type: string, handler: (payload: TPayload) => void) {
    this.eventHandlers.set(type, handler);
  }

  close() {
    this.ws.close();
  }
}
