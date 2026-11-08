import { useEffect, useMemo, useRef, useState } from 'react';
import { Box, Button, FileButton, Group, Loader, ScrollArea, Stack, Text, Tooltip } from '@mantine/core';
import { IconPhotoOff, IconUpload } from '@tabler/icons-react';
import { useWebSocket } from '../../state/websocket-context';
import { SocketRequests } from '../../socket/socket-requests';
import type { MediaImageKind } from '../../model/media.contract';
import { usePlugins } from '../../plugins/plugins.hook';
import { SourceFilter } from '../../plugins/source-filter';
import { useExcludedImageProviders } from '../../plugins/excluded-sources.store';
import type {
  ImageResult,
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
function useImageSearch(name: string, kind: MediaImageKind, itemId: string | undefined) {
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
      client.sendEvent<MediaImageSearchRequest>(SocketRequests.MediaImageSearch, {
        name: trimmed,
        kind,
        itemId: itemId ?? null,
        requestId,
      });
    }, SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [client, isClientReady, name, kind, itemId]);

  return { providers, loading };
}

/** Image providers of `kind` (user's plugin order) for the filter chips, and the ones the user left on. */
function useImageProviders(kind: MediaImageKind) {
  const plugins = usePlugins();
  const { excluded, setExcluded } = useExcludedImageProviders();
  const all = useMemo(() => plugins?.filter((p) => p.imageKinds?.includes(kind)) ?? [], [plugins, kind]);
  const enabled = useMemo(() => all.filter((p) => !excluded[p.id]), [all, excluded]);
  return { all, enabled, setEnabled: (id: string, on: boolean) => setExcluded(id, !on) };
}

/** Consecutive images of one title (`ImageResult.group`); one untitled run when the provider found a single title. */
function groupImages(images: ImageResult[]) {
  const groups: { title: string | null; images: ImageResult[] }[] = [];
  for (const image of images) {
    const last = groups[groups.length - 1];
    if (last && last.title === image.group) last.images.push(image);
    else groups.push({ title: image.group, images: [image] });
  }
  return groups;
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
  /** Library item being edited: providers get the media it was added from (exact title instead of a name search). */
  itemId?: string;
}

/** Poster or backdrop chooser: preview, own file upload, a "no image" choice and the images found by image provider plugins. */
export function MediaImagePicker({ kind, name, value, onChange, autoSelect, itemId }: MediaImagePickerProps) {
  const { providers: allResults, loading } = useImageSearch(name, kind, itemId);
  const filter = useImageProviders(kind);
  // Results of the providers the user left on; a provider missing from the plugin list (still loading) is shown.
  const providers = useMemo(
    () =>
      allResults?.filter(
        (r) => filter.enabled.some((p) => p.id === r.pluginId) || !filter.all.some((p) => p.id === r.pluginId)
      ) ?? null,
    [allResults, filter.enabled, filter.all]
  );
  const [uploadError, setUploadError] = useState<string | null>(null);
  const userPicked = useRef(false);
  const [previewW, previewH] = SIZES[kind].preview;
  const [thumbW, thumbH] = SIZES[kind].thumb;
  const label = kind === 'Poster' ? 'Poster' : 'Backdrop';

  useEffect(() => {
    if (!autoSelect || userPicked.current || !providers) return;
    onChange(providers.flatMap((p) => p.images)[0]?.url ?? null);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- only when new results arrive or the filter changes
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

      <Stack gap={6} style={{ flex: 1, minWidth: 0 }}>
        <Box mih={22}>
          <SourceFilter sources={filter.all} enabled={filter.enabled} onChange={filter.setEnabled} />
        </Box>
        <ScrollArea.Autosize mah={previewH + 4} type="auto" offsetScrollbars>
          <Stack gap="xs">
            {!found && noImageTile}
            {loading && !providers && <Loader size="sm" />}
            {providers?.length === 0 && (
              <Text size="sm" c="dimmed">
                {allResults && allResults.length > 0
                  ? 'All image providers are switched off above. Upload your own picture.'
                  : 'No image providers enabled (Settings → Plugins). Upload your own picture.'}
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
                {groupImages(provider.images).map((group, groupIndex) => (
                  <Stack key={`${groupIndex}:${group.title}`} gap={4}>
                    {group.title && (
                      <Text size="xs" fw={500}>
                        {group.title}
                      </Text>
                    )}
                    <Group gap={6}>
                      {index === firstWithImages && groupIndex === 0 && noImageTile}
                      {group.images.map((image) => (
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
            ))}
          </Stack>
        </ScrollArea.Autosize>
      </Stack>
    </Group>
  );
}
