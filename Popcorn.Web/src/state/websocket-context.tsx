import React, { createContext, useContext, useEffect, useRef, useState } from 'react';
import { WebSocketClient } from '../socket/websocket-client';
import { resolveServerHost } from '../utils/url-resolver';

export const wsAddress = `ws://${resolveServerHost()}/ws`;

type WebSocketContextValue = {
  client: WebSocketClient;
  isClientReady: boolean;
};

const WebSocketContext = createContext<WebSocketContextValue | null>(null);

export const useWebSocket = () => {
  const context = useContext(WebSocketContext);
  if (!context) throw new Error('WebSocketContext not initialized');
  return context;
};

export const WebSocketProvider = ({ children }: React.PropsWithChildren) => {
  const wsClientRef = useRef<WebSocketClient | null>(null);
  const [isClientReady, setIsClientReady] = useState(false);

  if (!wsClientRef.current) {
    wsClientRef.current = new WebSocketClient(wsAddress);
  }

  useEffect(() => {
    const client = wsClientRef.current!;
    const handleReadyChange = (ready: boolean) => setIsClientReady(ready);

    setIsClientReady(client.isReady); // in case it connects immediately
    client.onReadyChange(handleReadyChange);
    return () => client.onReadyChangeDelete(handleReadyChange);
  }, []);

  return (
    <WebSocketContext.Provider value={{ client: wsClientRef.current!, isClientReady }}>
      {children}
    </WebSocketContext.Provider>
  );
};
