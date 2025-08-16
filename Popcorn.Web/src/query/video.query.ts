import { useQuery } from '@tanstack/react-query';
import { querySetupGet } from './client/query.client';
import { MediaFileMetadata } from './models/video.models';

export function getVideoMetadata(fileId: string) {
  return useQuery(
    querySetupGet<MediaFileMetadata>(['video-metadata', fileId], `api/video/${fileId}/metadata`, {
      enabled: !!fileId,
    })
  );
}
