import { useEffect, useMemo, useState } from 'react';
import { MultiSelect, Stack, Text, Title } from '@mantine/core';
import { LanguageOption, Settings } from '../../model/settings.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';

interface Props {
  settings: Settings;
  onChange: (settings: Settings) => void;
}

/** Preferred audio / subtitle languages (ISO 639-2 codes, in order), used by every player to pick the start tracks. */
const LanguageSettings = ({ settings, onChange }: Props) => {
  const { client, isClientReady } = useWebSocket();
  const [languages, setLanguages] = useState<LanguageOption[]>([]);

  useEffect(() => {
    if (!isClientReady) return;
    const unsub = client.onEvent<LanguageOption[]>(SocketRequests.LanguageList, setLanguages);
    client.sendEvent(SocketRequests.LanguageList);
    return unsub;
  }, [isClientReady]);

  const data = useMemo(() => {
    const options = languages.map((l) => ({ value: l.code, label: l.name }));
    // Codes saved by hand that the list does not know stay visible (and removable).
    const known = new Set(options.map((o) => o.value));
    const saved = [...(settings.prefferedAudioLanguages ?? []), ...(settings.prefferedSubtitleLanguages ?? [])];
    for (const code of saved) {
      if (!known.has(code)) {
        known.add(code);
        options.push({ value: code, label: code });
      }
    }
    return options;
  }, [languages, settings.prefferedAudioLanguages, settings.prefferedSubtitleLanguages]);

  return (
    <Stack mt={30} maw={720} gap="xs">
      <Title order={3}>Languages</Title>
      <Text size="sm" c="dimmed">
        The players start with the first language in the list that the video has, in both the browser and the Popcorn
        player. Videos with none of them keep their default tracks.
      </Text>
      <MultiSelect
        label="Audio"
        description="In order of preference"
        placeholder="File default"
        data={data}
        value={settings.prefferedAudioLanguages ?? []}
        onChange={(prefferedAudioLanguages) => onChange({ ...settings, prefferedAudioLanguages })}
        searchable
        clearable
        hidePickedOptions
      />
      <MultiSelect
        label="Subtitles"
        description="A subtitle track in one of these is turned on at the start, in order of preference"
        placeholder="Off (file default)"
        data={data}
        value={settings.prefferedSubtitleLanguages ?? []}
        onChange={(prefferedSubtitleLanguages) => onChange({ ...settings, prefferedSubtitleLanguages })}
        searchable
        clearable
        hidePickedOptions
      />
    </Stack>
  );
};

export default LanguageSettings;
