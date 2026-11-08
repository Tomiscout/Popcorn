import { Chip, Group } from '@mantine/core';
import { PluginIcon, type Provider } from './provider-group';

/** One chip per provider (file source, image provider: icon + name) that switches it on or off; nothing with a single one. */
export function SourceFilter<T extends Provider>({
  sources,
  enabled,
  onChange,
}: {
  sources: T[];
  enabled: T[];
  onChange: (id: string, on: boolean) => void;
}) {
  if (sources.length < 2) return null;
  return (
    <Group gap={6}>
      {sources.map((source) => (
        <Chip
          key={source.id}
          size="xs"
          variant="outline"
          checked={enabled.includes(source)}
          onChange={(on) => onChange(source.id, on)}
        >
          <Group gap={4} wrap="nowrap">
            <PluginIcon plugin={source} size={14} />
            {source.name}
          </Group>
        </Chip>
      ))}
    </Group>
  );
}
