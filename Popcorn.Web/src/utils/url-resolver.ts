export const resolveServerHost = () => {
  const isDevelop = import.meta.env.DEV; // If it runs from vite server
  return isDevelop ? 'localhost:5000' : window.location.host;
};
