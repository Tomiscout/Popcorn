import React, { createContext, useContext, useEffect, useRef, useState } from 'react';
import { WebSocketClient } from '../socket/websocket-client';

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
    const isDevelop = import.meta.env.DEV; // If it runs from vite server
    const url = isDevelop ? 'ws://localhost:5000/ws' : `ws://${window.location.host}/ws`;
    wsClientRef.current = new WebSocketClient(url);
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
