import React, { createContext, useContext, useRef } from 'react';
import { WebSocketClient } from './websocket-client';

const WebSocketContext = createContext<WebSocketClient | null>(null);

export const useWebSocket = () => {
  const context = useContext(WebSocketContext);
  if (!context) throw new Error('WebSocketContext not initialized');
  return context;
};

export const WebSocketProvider = ({ children }: React.PropsWithChildren) => {
  const wsClientRef = useRef<WebSocketClient>(null);

  if (!wsClientRef.current) {
    wsClientRef.current = new WebSocketClient('ws://localhost:5000/ws');
  }

  return (
    <WebSocketContext.Provider value={wsClientRef.current}>
      {children}
    </WebSocketContext.Provider>
  );
};
