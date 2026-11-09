import React, { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { ActionIcon, Alert, Group, Loader, Text, TextInput, Tooltip, UnstyledButton } from '@mantine/core';
import { DatePickerInput, DatesRangeValue } from '@mantine/dates';
import '@mantine/dates/styles.css';
import { useDebouncedValue, useLocalStorage } from '@mantine/hooks';
import { useVirtualizer } from '@tanstack/react-virtual';
import {
  IconArrowBarToDown,
  IconCalendar,
  IconFolderOpen,
  IconRefresh,
  IconSearch,
  IconTextWrap,
  IconX,
} from '@tabler/icons-react';
import dayjs from 'dayjs';
import { LogApp, LogEntry, LogQuery, LogQueryResult, logLevels } from '../../../model/logs.contract';
import { resolveServerHost } from '../../../utils/url-resolver';
import { isAbortError, readJsonLines } from '../../../utils/json-lines';
import { appColor, levelColor } from './log-colors';
import './log-viewer.css';

/** Newest entries of a full query; live updates append, the oldest go beyond MAX_ENTRIES. */
const LIMIT = 5000;
const MAX_ENTRIES = 20000;

interface ViewEntry extends LogEntry {
  id: number;
}

type Row = { kind: 'day'; key: string; day: string } | { kind: 'entry'; key: string; entry: ViewEntry };

interface Filters {
  excludeApps: string[];
  excludeLevels: string[];
}

const api = (path: string) => `${window.location.protocol}//${resolveServerHost()}/api/logs/${path}`;

const postQuery = async (query: LogQuery, signal?: AbortSignal): Promise<LogQueryResult> => {
  const response = await fetch(api('query'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(query),
    signal,
  });
  if (!response.ok) throw new Error(`Server answered ${response.status}`);
  return response.json();
};

const addCounts = (a: Record<string, number>, b: Record<string, number>) => {
  const sum = { ...a };
  for (const [k, v] of Object.entries(b)) sum[k] = (sum[k] ?? 0) + v;
  return sum;
};

const formatCount = (n: number) => (n >= 10000 ? `${Math.round(n / 1000)}k` : n.toLocaleString());

const LogViewer: React.FC = () => {
  const [range, setRange] = useState<DatesRangeValue<string>>([null, null]);
  const [search, setSearch] = useState('');
  const [debouncedSearch] = useDebouncedValue(search.trim(), 300);
  const [filters, setFilters] = useLocalStorage<Filters>({
    key: 'popcorn.logs.filters',
    defaultValue: { excludeApps: [], excludeLevels: ['VRB', 'DBG'] },
  });
  const [wrap, setWrap] = useLocalStorage({ key: 'popcorn.logs.wrap', defaultValue: true });
  const [paused, setPaused] = useState(false);

  const [entries, setEntries] = useState<ViewEntry[]>([]);
  const [apps, setApps] = useState<LogApp[]>([]);
  const [oldestDay, setOldestDay] = useState<string | null>(null);
  const [levelCounts, setLevelCounts] = useState<Record<string, number>>({});
  const [appCounts, setAppCounts] = useState<Record<string, number>>({});
  const [skipped, setSkipped] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<number>>(new Set());
  const [unseen, setUnseen] = useState(0);
  const [reloadKey, setReloadKey] = useState(0);

  // A range with an end day is history: nothing new can arrive in it.
  const hasEnd = range[1] !== null;
  const live = !hasEnd && !paused;

  const query = useMemo<LogQuery>(
    () => ({
      from: range[0],
      to: range[1],
      excludeApps: filters.excludeApps,
      excludeLevels: filters.excludeLevels,
      search: debouncedSearch || undefined,
    }),
    [range, filters, debouncedSearch]
  );

  const cursorRef = useRef<Record<string, number> | null>(null);
  /** The query of the last full load: live updates continue it from `cursorRef`. */
  const loadedQueryRef = useRef<LogQuery | null>(null);
  const [loadedGeneration, setLoadedGeneration] = useState(0);
  const generationRef = useRef(0);
  const nextIdRef = useRef(0);
  const atBottomRef = useRef(true);

  const withIds = (list: LogEntry[]): ViewEntry[] => list.map((e) => ({ ...e, id: nextIdRef.current++ }));

  const applyMeta = (result: LogQueryResult) => {
    setApps(result.apps);
    setOldestDay(result.oldestDay ?? null);
  };

  // Full load on every filter change.
  useEffect(() => {
    const generation = ++generationRef.current;
    const abort = new AbortController();
    setLoading(true);
    postQuery({ ...query, limit: LIMIT }, abort.signal)
      .then((result) => {
        if (generation !== generationRef.current) return;
        cursorRef.current = result.cursor;
        loadedQueryRef.current = query;
        setLoadedGeneration(generation);
        atBottomRef.current = true;
        setEntries(withIds(result.entries));
        setExpanded(new Set());
        setUnseen(0);
        setLevelCounts(result.levelCounts);
        setAppCounts(result.appCounts);
        setSkipped(result.skipped);
        applyMeta(result);
        setError(null);
      })
      .catch((e: Error) => {
        if (generation === generationRef.current && e.name !== 'AbortError') setError(e.message);
      })
      .finally(() => {
        if (generation !== generationRef.current) return;
        setLoading(false);
      });
    return () => abort.abort();
  }, [query, reloadKey]);

  // Live: what gets appended to the files after the full load (new files too: the player's first log, a new day's),
  // sent by the server as it is written.
  useEffect(() => {
    const query = loadedQueryRef.current;
    if (!live || !query || !cursorRef.current || loadedGeneration !== generationRef.current) return;
    const generation = loadedGeneration;
    const abort = new AbortController();
    readJsonLines<LogQueryResult>(
      api('live'),
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...query, cursor: cursorRef.current }),
        signal: abort.signal,
      },
      (result) => {
        if (generation !== generationRef.current) return;
        cursorRef.current = result.cursor;
        applyMeta(result);
        const added = withIds(result.entries);
        setEntries((current) => {
          const merged = current.concat(added);
          return merged.length > MAX_ENTRIES ? merged.slice(merged.length - MAX_ENTRIES) : merged;
        });
        setLevelCounts((c) => addCounts(c, result.levelCounts));
        setAppCounts((c) => addCounts(c, result.appCounts));
        if (!atBottomRef.current) setUnseen((n) => n + added.length);
        setError(null);
      }
    ).catch((e) => {
      if (!isAbortError(e)) setError((e as Error).message);
    });
    return () => abort.abort();
  }, [live, loadedGeneration]);

  const rows = useMemo<Row[]>(() => {
    const list: Row[] = [];
    let day = '';
    for (const entry of entries) {
      const entryDay = entry.time.slice(0, 10);
      if (entryDay !== day) {
        day = entryDay;
        list.push({ kind: 'day', key: `day-${entry.id}`, day });
      }
      list.push({ kind: 'entry', key: `e-${entry.id}`, entry });
    }
    return list;
  }, [entries]);

  const scrollRef = useRef<HTMLDivElement>(null);
  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: (i) => (rows[i]?.kind === 'day' ? 36 : 22),
    getItemKey: (i) => rows[i].key,
    overscan: 24,
  });

  // Day of the first row in view, for the panel header (the day separators scroll away).
  const firstVisible = virtualizer.getVirtualItems().find((item) => item.end > (virtualizer.scrollOffset ?? 0));
  const firstRow = firstVisible ? rows[firstVisible.index] : undefined;
  const topDay = firstRow ? (firstRow.kind === 'day' ? firstRow.day : firstRow.entry.time.slice(0, 10)) : null;

  const scrollToEnd = useCallback(() => {
    if (rows.length === 0) return;
    atBottomRef.current = true;
    setUnseen(0);
    virtualizer.scrollToIndex(rows.length - 1, { align: 'end' });
    // Rows measured on the way may have moved the end: once more on the next frame.
    requestAnimationFrame(() => {
      const el = scrollRef.current;
      if (el && atBottomRef.current) el.scrollTop = el.scrollHeight;
    });
  }, [rows.length, virtualizer]);

  useLayoutEffect(() => {
    if (atBottomRef.current) scrollToEnd();
  }, [rows, wrap, scrollToEnd]);

  const onScroll = () => {
    const el = scrollRef.current;
    if (!el) return;
    const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 48;
    atBottomRef.current = atBottom;
    if (atBottom) setUnseen(0);
  };

  const toggle = (list: 'excludeApps' | 'excludeLevels', value: string, all: string[], solo: boolean) => {
    setFilters((f) => {
      const current = f[list];
      let next: string[];
      if (solo) {
        const isSolo = current.length === all.length - 1 && !current.includes(value);
        next = isSolo ? [] : all.filter((v) => v !== value);
      } else {
        next = current.includes(value) ? current.filter((v) => v !== value) : [...current, value];
      }
      return { ...f, [list]: next };
    });
  };

  const toggleExpanded = (id: number) => {
    if (window.getSelection()?.toString()) return; // selecting text, not clicking
    setExpanded((s) => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const today = dayjs().format('YYYY-MM-DD');
  const presets = [
    { value: [today, null] as [string, null], label: 'Today' },
    { value: [dayjs().subtract(1, 'day').format('YYYY-MM-DD'), dayjs().subtract(1, 'day').format('YYYY-MM-DD')] as [string, string], label: 'Yesterday' },
    { value: [dayjs().subtract(6, 'day').format('YYYY-MM-DD'), null] as [string, null], label: 'Last 7 days' },
    { value: [dayjs().subtract(29, 'day').format('YYYY-MM-DD'), null] as [string, null], label: 'Last 30 days' },
    { value: [null, null] as [null, null], label: 'Everything' },
  ];

  const allLevels = logLevels.map((l) => l.value);
  const allApps = apps.map((a) => a.name);
  const appLabel = (name: string) => apps.find((a) => a.name === name)?.label ?? name;

  return (
    <div className="log-viewer">
      <Group gap="sm" wrap="wrap" align="flex-end">
        <DatePickerInput
          type="range"
          value={range}
          onChange={setRange}
          allowSingleDateInRange
          clearable
          minDate={oldestDay ?? undefined}
          maxDate={today}
          presets={presets}
          valueFormat="D MMM YYYY"
          placeholder="All days"
          leftSection={<IconCalendar size={16} />}
          w={290}
          label="Days"
        />
        <TextInput
          label="Search"
          placeholder="Text in message or stack trace"
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
          leftSection={<IconSearch size={16} />}
          rightSection={
            search ? (
              <ActionIcon variant="subtle" color="gray" size="sm" onClick={() => setSearch('')}>
                <IconX size={14} />
              </ActionIcon>
            ) : null
          }
          style={{ flex: 1, minWidth: 220 }}
        />
        <Group gap={6} pb={2}>
          <LiveBadge hasEnd={hasEnd} paused={paused} onToggle={() => setPaused((p) => !p)} />
          <Tooltip label={wrap ? 'Long lines wrap' : 'Long lines are cut'}>
            <ActionIcon variant={wrap ? 'light' : 'subtle'} color="gray" size="lg" onClick={() => setWrap((w) => !w)}>
              <IconTextWrap size={18} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Reload">
            <ActionIcon variant="subtle" color="gray" size="lg" onClick={() => setReloadKey((k) => k + 1)}>
              <IconRefresh size={18} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Open the logs folder">
            <ActionIcon
              variant="subtle"
              color="gray"
              size="lg"
              onClick={() => fetch(api('open-folder'), { method: 'POST' })}
            >
              <IconFolderOpen size={18} />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      <div className="log-filters">
        <div className="log-chip-group">
          {logLevels.map((level) => (
            <FilterChip
              key={level.value}
              color={levelColor(level.value)}
              label={level.label}
              count={levelCounts[level.value] ?? 0}
              active={!filters.excludeLevels.includes(level.value)}
              onClick={(solo) => toggle('excludeLevels', level.value, allLevels, solo)}
            />
          ))}
        </div>
        <div className="log-chip-group">
          {apps.map((app) => (
            <FilterChip
              key={app.name}
              color={appColor(app.name)}
              label={app.label}
              title={app.name}
              count={appCounts[app.name] ?? 0}
              active={!filters.excludeApps.includes(app.name)}
              onClick={(solo) => toggle('excludeApps', app.name, allApps, solo)}
            />
          ))}
        </div>
      </div>

      {error && (
        <Alert color="red" variant="light" mb="xs" title="Cannot read the logs">
          {error}
        </Alert>
      )}

      <div className={`log-panel${wrap ? ' wrap' : ''}`}>
        <div className="log-panel-head">
          <span>
            {entries.length.toLocaleString()} {entries.length === 1 ? 'entry' : 'entries'}
            {skipped > 0 && <span className="log-dim"> · {skipped.toLocaleString()} older not shown, narrow the days</span>}
          </span>
          {topDay && <span className="log-head-day">{dayjs(topDay).format('dddd, D MMMM YYYY')}</span>}
          <span className="log-dim">Ctrl+click a filter to show only it</span>
        </div>
        <div className="log-scroll" ref={scrollRef} onScroll={onScroll}>
          {!loading && entries.length === 0 && (
            <div className="log-empty">
              <Text size="sm">No log entries match.</Text>
            </div>
          )}
          <div style={{ height: virtualizer.getTotalSize(), position: 'relative', width: '100%' }}>
            {virtualizer.getVirtualItems().map((item) => {
              const row = rows[item.index];
              return (
                <div
                  key={item.key}
                  data-index={item.index}
                  ref={virtualizer.measureElement}
                  className="log-virtual-row"
                  style={{ transform: `translateY(${item.start}px)` }}
                >
                  {row.kind === 'day' ? (
                    <DaySeparator day={row.day} />
                  ) : (
                    <LogRow
                      entry={row.entry}
                      appLabel={appLabel(row.entry.app)}
                      search={debouncedSearch}
                      expanded={expanded.has(row.entry.id)}
                      onToggle={() => toggleExpanded(row.entry.id)}
                    />
                  )}
                </div>
              );
            })}
          </div>
        </div>
        {loading && (
          <div className="log-loading">
            <Loader size="sm" color="primaryRed" />
          </div>
        )}
        {unseen > 0 && (
          <UnstyledButton className="log-jump" onClick={scrollToEnd}>
            <IconArrowBarToDown size={14} /> {formatCount(unseen)} new
          </UnstyledButton>
        )}
      </div>
    </div>
  );
};

const LiveBadge: React.FC<{ hasEnd: boolean; paused: boolean; onToggle: () => void }> = ({
  hasEnd,
  paused,
  onToggle,
}) => {
  const state = hasEnd ? 'off' : paused ? 'paused' : 'on';
  const label = { on: 'Live', paused: 'Paused', off: 'Live off' }[state];
  const tip = {
    on: 'New lines appear as the apps write them. Click to pause.',
    paused: 'Click to resume live updates.',
    off: 'The day range has an end: live updates are off. Clear the end day to turn them on.',
  }[state];
  return (
    <Tooltip label={tip} multiline w={240}>
      <UnstyledButton className={`log-live ${state}`} onClick={hasEnd ? undefined : onToggle}>
        <span className="log-live-dot" />
        {label}
      </UnstyledButton>
    </Tooltip>
  );
};

const FilterChip: React.FC<{
  color: string;
  label: string;
  title?: string;
  count: number;
  active: boolean;
  onClick: (solo: boolean) => void;
}> = ({ color, label, title, count, active, onClick }) => (
  <UnstyledButton
    className={`log-chip${active ? ' active' : ''}`}
    style={{ '--chip-color': color } as React.CSSProperties}
    title={title}
    onClick={(e) => onClick(e.ctrlKey || e.metaKey)}
  >
    <span className="log-chip-dot" />
    {label}
    <span className="log-chip-count">{formatCount(count)}</span>
  </UnstyledButton>
);

const DaySeparator: React.FC<{ day: string }> = ({ day }) => {
  const d = dayjs(day);
  const today = dayjs().startOf('day');
  const name = d.isSame(today, 'day') ? 'Today' : d.isSame(today.subtract(1, 'day'), 'day') ? 'Yesterday' : d.format('dddd');
  return (
    <div className="log-day">
      <span>
        {name} <span className="log-dim">· {d.format('D MMMM YYYY')}</span>
      </span>
    </div>
  );
};

const highlight = (text: string, search: string): React.ReactNode => {
  if (!search) return text;
  const lower = text.toLowerCase();
  const needle = search.toLowerCase();
  const parts: React.ReactNode[] = [];
  let at = 0;
  let i = lower.indexOf(needle);
  while (i >= 0) {
    if (i > at) parts.push(text.slice(at, i));
    parts.push(<mark key={i}>{text.slice(i, i + needle.length)}</mark>);
    at = i + needle.length;
    i = lower.indexOf(needle, at);
  }
  if (at < text.length) parts.push(text.slice(at));
  return parts;
};

/** `[native] ...`, `[overlay] ...` (player) → tag + message. */
const tagPattern = /^\[([\w.-]{1,24})\]\s+/;

const LogRow: React.FC<{
  entry: ViewEntry;
  appLabel: string;
  search: string;
  expanded: boolean;
  onToggle: () => void;
}> = React.memo(({ entry, appLabel, search, expanded, onToggle }) => {
  const color = levelColor(entry.level);
  const tag = tagPattern.exec(entry.message);
  const message = tag ? entry.message.slice(tag[0].length) : entry.message;
  const detailLines = entry.detail ? entry.detail.split('\n').length : 0;

  return (
    <div
      className={`log-row lvl-${entry.level}${entry.detail ? ' has-detail' : ''}`}
      style={{ '--lvl-color': color, '--app-color': appColor(entry.app) } as React.CSSProperties}
      onClick={entry.detail ? onToggle : undefined}
    >
      <div className="log-line">
        <span className="log-time" title={entry.time}>
          {entry.time.slice(11, 19)}
          <span className="log-ms">{entry.time.slice(19, 23)}</span>
        </span>
        <span className="log-level">{entry.level}</span>
        <span className="log-app" title={entry.app}>
          {appLabel}
        </span>
        <span className="log-msg">
          {tag && <span className="log-tag">{tag[1]}</span>}
          {highlight(message, search)}
          {entry.source && <span className="log-source">{entry.source}</span>}
          {entry.detail && (
            <span className="log-more">
              {expanded ? '▾' : '▸'} {detailLines} {detailLines === 1 ? 'line' : 'lines'}
            </span>
          )}
        </span>
      </div>
      {expanded && entry.detail && <pre className="log-detail" onClick={(e) => e.stopPropagation()}>{highlight(entry.detail, search)}</pre>}
    </div>
  );
});

export default LogViewer;
