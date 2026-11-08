import { useQuery } from '@tanstack/react-query';
import { useQuerySetupGet } from './client/query.client';
import { MediaFileMetadata } from './models/video.models';

export function useVideoMetadata(fileId: string) {
  return useQuery(
    useQuerySetupGet<MediaFileMetadata>(['video-metadata', fileId], `api/video/${fileId}/metadata`, {
      enabled: !!fileId,
    })
  );
}
