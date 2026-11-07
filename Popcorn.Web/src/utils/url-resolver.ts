const BACKEND_PORT = 5000;

export const resolveServerHost = () => {
  const isDevelop = import.meta.env.DEV; // If it runs from vite server
  return isDevelop ? `${window.location.host.split(':')[0]}:${BACKEND_PORT}` : window.location.host;
};
