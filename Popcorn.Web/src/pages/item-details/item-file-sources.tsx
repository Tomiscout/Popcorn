import { Button, FileButton, Group, Paper, Stack, Text, Textarea, TextInput } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { IconDownload, IconSearch, IconUpload } from '@tabler/icons-react';
import { useMemo, useState } from 'react';
import { DownloadKind } from '../../model/download.contract';
import { MediaItem } from '../../model/media.contract';
import { FileResults } from '../../plugins/file-results';
import { FileSearch, LinkMedia, searchLabel, useFileSearch } from '../../plugins/file-sources';
import { SourceFilter } from '../../plugins/source-filter';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';

/** Kind of a pasted link: magnet: or a .torrent URL = torrent, other http(s) = the file itself; null = unusable. */
function linkKind(url: string): DownloadKind | null {
  if (/^magnet:/i.test(url)) return 'Torrent';
  if (!/^https?:\/\//i.test(url)) return null;
  try {
    return new URL(url).pathname.toLowerCase().endsWith('.torrent') ? 'Torrent' : 'Direct';
  } catch {
    return null;
  }
}

/** Links pasted one per line (or space separated) and .torrent files, all downloaded into the item. */
function AddLinks({ itemId }: { itemId: string }) {
  const { client } = useWebSocket();
  const [text, setText] = useState('');
  const links = useMemo(
    () =>
      text
        .split(/\s+/)
        .filter(Boolean)
        .map((url) => ({ url, kind: linkKind(url) })),
    [text]
  );
  const valid = links.filter((l): l is { url: string; kind: DownloadKind } => l.kind !== null);
  const torrents = valid.filter((l) => l.kind === 'Torrent').length;

  const add = () => {
    if (valid.length === 0) return;
    client.sendEvent(SocketRequests.DownloadAddBatch, { links: valid, mediaItemId: itemId });
    setText('');
  };

  const upload = async (files: File[]) => {
    for (const file of files) {
      const bytes = new Uint8Array(await file.arrayBuffer());
      client.sendBinaryEvent(SocketRequests.DownloadAddTorrentFile, bytes, '1', '', itemId);
    }
    if (files.length > 1) notifications.show({ message: `Adding ${files.length} .torrent files` });
  };

  return (
    <Paper withBorder p="md" radius="md">
      <Stack gap="xs">
        <Textarea
          label="Add links"
          description="Magnet links, .torrent URLs or direct file URLs, one per line: the kind is detected per link"
          placeholder={'magnet:?xt=urn:btih:…\nhttps://…/episode-01.mkv'}
          autosize
          minRows={2}
          maxRows={8}
          value={text}
          onChange={(e) => setText(e.currentTarget.value)}
        />
        <Group gap="sm">
          <Button leftSection={<IconDownload size={16} />} disabled={valid.length === 0} onClick={add}>
            Download {valid.length > 1 ? valid.length : ''}
          </Button>
          <FileButton onChange={upload} accept=".torrent,application/x-bittorrent" multiple>
            {(props) => (
              <Button variant="light" leftSection={<IconUpload size={16} />} {...props}>
                .torrent files
              </Button>
            )}
          </FileButton>
          {links.length > 0 && (
            <Text size="sm" c="dimmed">
              {torrents > 0 && `${torrents} torrent${torrents === 1 ? '' : 's'}`}
              {torrents > 0 && valid.length > torrents && ', '}
              {valid.length > torrents && `${valid.length - torrents} direct`}
              {links.length > valid.length && ` · ${links.length - valid.length} not a link, ignored`}
            </Text>
          )}
        </Group>
      </Stack>
    </Paper>
  );
}

/**
 * File sources tab of a library item: search the file source plugins (or paste links / pick .torrent files) and
 * download more files into the item, e.g. missing episodes. Downloads go into a folder inside the item's folder.
 */
export function ItemFileSources({ item, initialQuery }: { item: MediaItem; initialQuery?: string }) {
  const title = item.shortTitle || item.title;
  const [text, setText] = useState(initialQuery ?? title);
  const [search, setSearch] = useState<FileSearch | null>(null);
  const { sources, enabled, setEnabled, start, refresh } = useFileSearch(search, setSearch);
  const media = useMemo<LinkMedia>(() => ({ title, itemId: item.id }), [title, item.id]);
  const query = text.trim();

  return (
    <Stack gap="md" p="sm">
      <AddLinks itemId={item.id} />
      {sources?.length ? (
        <Stack gap="xs">
          <Group gap="sm" align="flex-end">
            <TextInput
              style={{ flex: 1, maxWidth: 480 }}
              label="Search file sources"
              value={text}
              onChange={(e) => setText(e.currentTarget.value)}
              onKeyDown={(e) => e.key === 'Enter' && query && start({ text: query, title: query })}
            />
            <Button
              leftSection={<IconSearch size={16} />}
              disabled={!query || !enabled?.length}
              onClick={() => start({ text: query, title: query })}
            >
              {searchLabel(enabled ?? [])}
            </Button>
          </Group>
          <SourceFilter sources={sources} enabled={enabled ?? []} onChange={setEnabled} />
        </Stack>
      ) : (
        sources && (
          <Text c="dimmed" size="sm">
            No file source plugins are enabled (Settings → Plugins).
          </Text>
        )
      )}
      {search && enabled && (
        <FileResults section="item-file-sources" sources={enabled} search={search} media={media} onRefresh={refresh} />
      )}
    </Stack>
  );
}
