import { useEffect, useRef, useState } from 'react';
import { Box, Button, FileButton, Group, Loader, ScrollArea, Stack, Text, Tooltip } from '@mantine/core';
import { IconPhotoOff, IconUpload } from '@tabler/icons-react';
import { useWebSocket } from '../../state/websocket-context';
import { SocketRequests } from '../../socket/socket-requests';
import type { MediaImageKind } from '../../model/media.contract';
import type {
  MediaImageProviderResult,
  MediaImageSearchRequest,
  MediaImageSearchResponse,
} from '../../model/plugin.contract';
import './media-image-picker.css';

const MAX_UPLOAD_BYTES = 20 * 1024 * 1024; // MediaImageService.MaxImageBytes
const SEARCH_DELAY_MS = 500;

const SIZES: Record<MediaImageKind, { preview: [number, number]; thumb: [number, number] }> = {
  Poster: { preview: [150, 225], thumb: [80, 120] },
  Backdrop: { preview: [320, 180], thumb: [160, 90] },
};

let requestCounter = 0;

/** Results of every enabled image provider of `kind` for `name` (debounced; answers to older searches dropped). */
function useImageSearch(name: string, kind: MediaImageKind) {
  const { client, isClientReady } = useWebSocket();
  const [providers, setProviders] = useState<MediaImageProviderResult[] | null>(null);
  const [loading, setLoading] = useState(false);
  const pending = useRef<string | null>(null);

  useEffect(() => {
    if (!isClientReady) return;
    return client.onEvent<MediaImageSearchResponse>(SocketRequests.MediaImageSearch, (response) => {
      if (response.requestId !== pending.current) return;
      pending.current = null;
      setProviders(response.providers);
      setLoading(false);
    });
  }, [client, isClientReady]);

  useEffect(() => {
    const trimmed = name.trim();
    if (!isClientReady || !trimmed) {
      pending.current = null;
      setProviders(null);
      setLoading(false);
      return;
    }
    setLoading(true);
    const timer = setTimeout(() => {
      const requestId = `${Date.now()}-${++requestCounter}`;
      pending.current = requestId;
      client.sendEvent<MediaImageSearchRequest>(SocketRequests.MediaImageSearch, { name: trimmed, kind, requestId });
    }, SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [client, isClientReady, name, kind]);

  return { providers, loading };
}

function readAsDataUrl(file: File) {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

interface MediaImagePickerProps {
  kind: MediaImageKind;
  /** What to search image providers for (item title or folder name). */
  name: string;
  /** Shown as the current image: http(s)/data: URL of a pick, or the stored image's object URL. Null = none. */
  value: string | null;
  /** http(s) URL (provider image), data: URL (uploaded file) or null (remove). */
  onChange(url: string | null): void;
  /** Pick the first result of each search until the user picks something themselves. */
  autoSelect?: boolean;
}

/** Poster or backdrop chooser: preview, own file upload, a "no image" choice and the images found by image provider plugins. */
export function MediaImagePicker({ kind, name, value, onChange, autoSelect }: MediaImagePickerProps) {
  const { providers, loading } = useImageSearch(name, kind);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const userPicked = useRef(false);
  const [previewW, previewH] = SIZES[kind].preview;
  const [thumbW, thumbH] = SIZES[kind].thumb;
  const label = kind === 'Poster' ? 'Poster' : 'Backdrop';

  useEffect(() => {
    if (!autoSelect || userPicked.current || !providers) return;
    onChange(providers.flatMap((p) => p.images)[0]?.url ?? null);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- only when new results arrive
  }, [providers, autoSelect]);

  const pick = (url: string | null) => {
    userPicked.current = true;
    onChange(url);
  };

  const upload = async (file: File | null) => {
    if (!file) return;
    if (file.size > MAX_UPLOAD_BYTES) {
      setUploadError('Images over 20 MB are not supported');
      return;
    }
    setUploadError(null);
    pick(await readAsDataUrl(file));
  };

  const firstWithImages = providers?.findIndex((p) => p.images.length > 0) ?? -1;
  const found = firstWithImages >= 0;

  const noImageTile = (
    <button
      type="button"
      className="media-image-thumb media-image-none"
      data-selected={!value || undefined}
      style={{ width: thumbW, height: thumbH }}
      onClick={() => pick(null)}
    >
      <IconPhotoOff size={20} stroke={1.5} />
      <Text span size="xs">
        No {label.toLowerCase()}
      </Text>
    </button>
  );

  return (
    <Group align="flex-start" wrap="nowrap" gap="md">
      <Stack gap={6} style={{ flexShrink: 0 }}>
        <Group justify="space-between">
          <Text fw={500} size="sm">
            {label}
          </Text>
          <FileButton onChange={upload} accept="image/jpeg,image/png,image/webp,image/gif">
            {(props) => (
              <Button {...props} size="compact-xs" variant="default" leftSection={<IconUpload size={12} />}>
                Upload
              </Button>
            )}
          </FileButton>
        </Group>
        <Box className="media-image-preview" w={previewW} h={previewH}>
          {value ? (
            <img src={value} alt={label} />
          ) : (
            <Stack align="center" justify="center" h="100%" gap={4} c="dimmed">
              <IconPhotoOff size={28} stroke={1.5} />
              <Text size="xs">No {label.toLowerCase()}</Text>
            </Stack>
          )}
        </Box>
        {uploadError && (
          <Text size="xs" c="red" maw={previewW}>
            {uploadError}
          </Text>
        )}
      </Stack>

      <ScrollArea.Autosize mah={previewH + 30} style={{ flex: 1, minWidth: 0 }} type="auto" offsetScrollbars>
        <Stack gap="xs" pt={28}>
          {!found && noImageTile}
          {loading && !providers && <Loader size="sm" />}
          {providers?.length === 0 && (
            <Text size="sm" c="dimmed">
              No image providers enabled (Settings → Plugins). Upload your own picture.
            </Text>
          )}
          {providers && providers.length > 0 && !found && !providers.some((p) => p.error) && (
            <Text size="sm" c="dimmed">
              Nothing found for “{name.trim()}”. Edit the title or upload your own picture.
            </Text>
          )}
          {providers?.map((provider, index) => (
            <Stack key={provider.pluginId} gap={4}>
              {(providers.length > 1 || provider.error) && (
                <Group gap="xs">
                  <Text size="xs" c="dimmed">
                    {provider.pluginName}
                  </Text>
                  {loading && <Loader size={10} />}
                </Group>
              )}
              {provider.error && (
                <Text size="xs" c="red">
                  {provider.error}
                </Text>
              )}
              <Group gap={6}>
                {index === firstWithImages && noImageTile}
                {provider.images.map((image) => (
                  <Tooltip key={image.url} label={image.title} disabled={!image.title} openDelay={400}>
                    <button
                      type="button"
                      className="media-image-thumb"
                      data-selected={image.url === value || undefined}
                      style={{ width: thumbW, height: thumbH }}
                      onClick={() => pick(image.url)}
                    >
                      <img src={image.thumbnailUrl ?? image.url} alt={image.title ?? ''} loading="lazy" />
                    </button>
                  </Tooltip>
                ))}
              </Group>
            </Stack>
          ))}
        </Stack>
      </ScrollArea.Autosize>
    </Group>
  );
}
