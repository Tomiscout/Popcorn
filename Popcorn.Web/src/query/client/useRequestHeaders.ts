import { v4 as uuidv4 } from 'uuid';

export const useRequestHeaders = () => {
  const headers = new Headers();

  headers.append('X-CorrelationId', uuidv4().replace(/-/g, ''));

  return headers;
};
