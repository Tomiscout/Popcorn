import { useMutation, useQuery } from '@tanstack/react-query';
import { querySetupGet, querySetupGetMutate } from './client/query.client';
import { MediaFileMetadata } from './models/video.models';
import { useRequestHeaders } from './client/useRequestHeaders';
import apiClient from './client/apiClient';

export function getVideoMetadata(fileId: string) {
  return useQuery(
    querySetupGet<MediaFileMetadata>(['video-metadata', fileId], `api/video/${fileId}/metadata`, {
      enabled: !!fileId,
    })
  );
}

export const useAudioDownloadMutation = (fileId: string) => {
  const headers = useRequestHeaders();

  return useMutation({
    mutationFn: async (params: { trackId: string }) => {
      return apiClient<Blob>(`api/video/${fileId}/audio/${params.trackId}`, {
        headers,
        method: 'GET',
      });
    },
  });
};
