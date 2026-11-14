import { Badge, Group, SegmentedControl, Select, Stack, Text, TextInput } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { useEffect, useMemo, useRef, useState } from 'react';
import { MediaItemMatch, MediaItemMatchResponse } from '../../model/media.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { useWebSocket } from '../../state/websocket-context';

/** Score from which the best match is preselected (title words in common, 0..1). */
const PRESELECT_SCORE = 0.5;

/**
 * Library items ranked for a release / file name by the server (`MediaItemMatch`), best first; null while loading.
 * `exclude` drops an item (the one being merged into).
 */
export function useItemMatches(name: string, enabled: boolean, exclude?: string): MediaItemMatch[] | null {
  const { client, isClientReady } = useWebSocket();
  const [debounced] = useDebouncedValue(name.trim(), 250);
  const [response, setResponse] = useState<MediaItemMatchResponse | null>(null);
  useSocketEvents(
    useMemo(() => ({ [SocketRequests.MediaItemMatch]: (r: MediaItemMatchResponse) => setResponse(r) }), [])
  );
  useEffect(() => {
    if (enabled && isClientReady) client.sendEvent(SocketRequests.MediaItemMatch, { name: debounced });
  }, [enabled, isClientReady, client, debounced]);
  return useMemo(
    () => (response && response.name === debounced ? response.items.filter((i) => i.id !== exclude) : null),
    [response, debounced, exclude]
  );
}

/** Existing library items, best match for the name first, labelled when they fit it. */
export function ItemSelect({
  matches,
  value,
  onChange,
  label = 'Library item',
}: {
  matches: MediaItemMatch[] | null;
  value: string | null;
  onChange: (id: string | null) => void;
  label?: string;
}) {
  const best = matches?.[0];
  const data = useMemo(() => (matches ?? []).map((m) => ({ value: m.id, label: m.title })), [matches]);
  const scores = useMemo(() => new Map((matches ?? []).map((m) => [m.id, m.score])), [matches]);
  return (
    <Select
      label={label}
      placeholder={matches === null ? 'Loading…' : 'Pick an item'}
      data={data}
      value={value}
      onChange={onChange}
      searchable
      nothingFoundMessage="No such item"
      maxDropdownHeight={320}
      renderOption={({ option }) => (
        <Group justify="space-between" wrap="nowrap" w="100%">
          <Text size="sm">{option.label}</Text>
          {option.value === best?.id && best.score >= PRESELECT_SCORE ? (
            <Badge size="xs" variant="light">
              Best match
            </Badge>
          ) : (scores.get(option.value) ?? 0) >= PRESELECT_SCORE ? (
            <Badge size="xs" variant="light" color="gray">
              Match
            </Badge>
          ) : null}
        </Group>
      )}
    />
  );
}

/** Where downloaded files go in the library. */
export type ItemTarget =
  | { kind: 'new'; title: string }
  | { kind: 'existing'; itemId: string | null }
  | { kind: 'none' };

type Mode = ItemTarget['kind'];

/**
 * "New item / Existing item / Not in library" picker of a download dialog. Existing items are ranked for `name`
 * (release or file name) and the best one is preselected (switching to Existing) until the user picks themselves.
 */
export function ItemTargetPicker({
  name,
  opened,
  value,
  onChange,
}: {
  name: string;
  opened: boolean;
  value: ItemTarget;
  onChange: (target: ItemTarget) => void;
}) {
  const matches = useItemMatches(name, opened);
  const touched = useRef(false);
  const [title, setTitle] = useState('');
  const [itemId, setItemId] = useState<string | null>(null);

  useEffect(() => {
    if (!opened) {
      touched.current = false;
      setTitle('');
      setItemId(null);
    }
  }, [opened]);

  // Preselect the best match while the user has not chosen.
  useEffect(() => {
    if (touched.current || !matches) return;
    const best = matches[0];
    if (best && best.score >= PRESELECT_SCORE) {
      setItemId(best.id);
      onChange({ kind: 'existing', itemId: best.id });
    } else if (value.kind === 'existing') {
      setItemId(null);
      onChange({ kind: 'new', title });
    }
    // onChange / value / title: only a new ranking preselects
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [matches]);

  const setMode = (mode: Mode) => {
    touched.current = true;
    onChange(
      mode === 'new' ? { kind: 'new', title } : mode === 'existing' ? { kind: 'existing', itemId } : { kind: 'none' }
    );
  };

  return (
    <Stack gap="xs">
      <SegmentedControl
        value={value.kind}
        onChange={(v) => setMode(v as Mode)}
        data={[
          { value: 'existing', label: 'Existing item' },
          { value: 'new', label: 'New item' },
          { value: 'none', label: 'Not in library' },
        ]}
      />
      {value.kind === 'existing' && (
        <ItemSelect
          matches={matches}
          value={itemId}
          onChange={(id) => {
            touched.current = true;
            setItemId(id);
            onChange({ kind: 'existing', itemId: id });
          }}
        />
      )}
      {value.kind === 'existing' && (
        <Text size="xs" c="dimmed">
          The files become a folder of their own in this item; they stay where they are downloaded.
        </Text>
      )}
      {value.kind === 'new' && (
        <TextInput
          label="Title"
          placeholder={name.trim() ? 'From the name (release tags dropped)' : 'Title in the library'}
          value={title}
          onChange={(e) => {
            touched.current = true;
            setTitle(e.currentTarget.value);
            onChange({ kind: 'new', title: e.currentTarget.value });
          }}
        />
      )}
    </Stack>
  );
}

/** The add request's library fields for a target. */
export function libraryFields(target: ItemTarget) {
  return {
    addToLibrary: target.kind !== 'none',
    mediaTitle: target.kind === 'new' ? target.title.trim() || null : null,
    mediaItemId: target.kind === 'existing' ? target.itemId : null,
  };
}

export const isTargetReady = (target: ItemTarget) => target.kind !== 'existing' || !!target.itemId;

/** A readable name of a link: magnet `dn`, else the URL's file name (no .torrent). */
export function linkName(url: string): string {
  const trimmed = url.trim();
  if (trimmed.toLowerCase().startsWith('magnet:')) {
    const query = trimmed.slice(trimmed.indexOf('?') + 1);
    return new URLSearchParams(query).get('dn') ?? '';
  }
  try {
    const last = new URL(trimmed).pathname.split('/').filter(Boolean).pop() ?? '';
    return decodeURIComponent(last).replace(/\.torrent$/i, '');
  } catch {
    return '';
  }
}
