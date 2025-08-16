import { resolveServerHost } from '../../utils/url-resolver';

declare module '@tanstack/react-query' {
  interface Register {
    defaultError: ApiError;
  }
}

export class ApiError<T = unknown> extends Error {
  constructor(
    public message: string,
    public status?: number | null,
    public correlationId?: string | null,
    public data?: T
  ) {
    super();
  }
}

const resolveErrorAsync = async (response: Response) => {
  try {
    return await response.clone().json();
  } catch {
    return response.clone().text();
  }
};

const resolveResponseAsync = async (response: Response) => {
  const contentType = response.headers.get('content-type');
  if (contentType?.includes('application/json')) return response.json();
  return response.text();
};

const apiClient = <TResult = unknown, TError = ApiError>(url: URL | RequestInfo, init?: RequestInit) => {
  const requestUrl = `http://${resolveServerHost()}/${url}`;
  const requestCorrelationId = (init?.headers as Headers).get('X-CorrelationId');

  return fetch(requestUrl, init)
    .then(async (response) => {
      if (response.ok) return (await resolveResponseAsync(response)) as TResult;

      const correlationId = response?.headers?.get('x-correlationid') || requestCorrelationId;

      throw new ApiError(
        `Request ${requestUrl} failed`,
        response.status,
        correlationId,
        (await resolveErrorAsync(response)) as TError
      );
    })
    .catch(async (error) => {
      if (error instanceof ApiError) {
        console.error(`HTTP Error ${error.status}`, error.data);
        throw error;
      } else {
        throw new ApiError(`Unknown HTTP error occured`, null, requestCorrelationId);
      }
    });
};

export default apiClient;
