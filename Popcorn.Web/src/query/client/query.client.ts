import { queryOptions, useMutation } from '@tanstack/react-query';
import { PartialUseQueryOptions } from './query';
import { useRequestHeaders } from './useRequestHeaders';
import apiClient from './apiClient';

export const useQuerySetupGet = <ResultType>(
  queryKey: string[],
  url: string,
  options: PartialUseQueryOptions<ResultType> = {}
) => {
  const headers = useRequestHeaders();

  return queryOptions({
    ...options,
    queryKey,
    queryFn: () => {
      return apiClient<ResultType>(url, { headers });
    },
  });
};

export const useQuerySetupGetMutate = <ResultType>(url: string) => {
  const headers = useRequestHeaders();

  return useMutation({
    mutationFn: () => {
      return apiClient<ResultType>(url, {
        headers,
        method: 'GET',
      });
    },
  });
};

export const useQuerySetupPost = <ResultType>(url: string) => {
  const headers = useRequestHeaders();
  headers.append('Content-Type', 'application/json');

  return useMutation({
    mutationFn: (data: any) => {
      return apiClient<ResultType>(url, {
        headers,
        method: 'POST',
        body: JSON.stringify(data),
      });
    },
  });
};
