import React, { useEffect, useMemo, useState } from 'react';
import {
  ActionIcon,
  Alert,
  Button,
  Fieldset,
  Group,
  Input,
  Modal,
  NumberInput,
  PasswordInput,
  Select,
  Skeleton,
  Stack,
  Switch,
  TextInput,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { IconPlus, IconTrash } from '@tabler/icons-react';
import {
  PluginInfo,
  PluginSettingField,
  PluginSettingsRequest,
  PluginSettingsResponse,
  PluginSettingsSaveRequest,
  PluginSettingsSaveResponse,
  PluginSettingValue,
} from '../../model/plugin.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { useWebSocket } from '../../state/websocket-context';

interface Props {
  plugin: PluginInfo;
  onClose: () => void;
}

/** List being edited: entries may be blank or (number inputs) unparsed text until saved. */
type ListDraft = (string | number)[];
type Draft = PluginSettingValue | ListDraft;

const isNumeric = (field: PluginSettingField) => field.type === 'number' || field.type === 'float';

/** Drops blank entries (and unparsed numbers) of a list before it is sent. */
const cleanList = (field: PluginSettingField, list: ListDraft): PluginSettingValue =>
  isNumeric(field)
    ? list.filter((item): item is number => typeof item === 'number')
    : list.filter((item): item is string => typeof item === 'string' && item.trim() !== '');

/** Fields split by `group`, groups in order of first use; fields without one form a group without heading. */
const groupFields = (fields: PluginSettingField[]) => {
  const groups = new Map<string | null, PluginSettingField[]>();
  for (const field of fields) groups.set(field.group, [...(groups.get(field.group) ?? []), field]);
  return [...groups];
};

interface ListInputProps {
  field: PluginSettingField;
  value: ListDraft;
  onChange: (value: ListDraft) => void;
  placeholder?: string;
}

/** Editable list of text / password / number inputs with add and remove buttons. */
const ListInput: React.FC<ListInputProps> = ({ field, value, onChange, placeholder }) => {
  const set = (index: number, item: string | number) => onChange(value.map((v, i) => (i === index ? item : v)));
  const renderItem = (item: string | number, index: number) => {
    const common = { placeholder, style: { flex: 1 }, 'aria-label': `${field.label} ${index + 1}` };
    if (isNumeric(field))
      return (
        <NumberInput
          {...common}
          allowDecimal={field.type === 'float'}
          value={item}
          onChange={(next) => set(index, next)}
        />
      );
    if (field.type === 'password')
      return (
        <PasswordInput
          {...common}
          autoComplete="new-password"
          value={String(item)}
          onChange={(e) => set(index, e.currentTarget.value)}
        />
      );
    return <TextInput {...common} value={String(item)} onChange={(e) => set(index, e.currentTarget.value)} />;
  };
  return (
    <Input.Wrapper label={field.label} description={field.description ?? undefined} withAsterisk={field.required}>
      <Stack gap={6} mt={4}>
        {value.map((item, index) => (
          <Group key={index} gap="xs" wrap="nowrap">
            {renderItem(item, index)}
            <ActionIcon
              variant="subtle"
              color="red"
              aria-label="Remove"
              onClick={() => onChange(value.filter((_, i) => i !== index))}
            >
              <IconTrash size={16} />
            </ActionIcon>
          </Group>
        ))}
        <Group>
          <Button
            variant="light"
            size="compact-sm"
            leftSection={<IconPlus size={14} />}
            onClick={() => onChange([...value, ''])}
          >
            Add
          </Button>
        </Group>
      </Stack>
    </Input.Wrapper>
  );
};

/**
 * Form generated from the `settings` fields of a plugin's plugin.json, shown under the fields' `group` headings. Saved
 * passwords are not sent to the browser: their inputs start empty and only a typed or removed password (list) goes back
 * to the server.
 */
const PluginSettingsDialog: React.FC<Props> = ({ plugin, onClose }) => {
  const { client } = useWebSocket();
  const [loaded, setLoaded] = useState<PluginSettingsResponse | null>(null);
  const [values, setValues] = useState<Record<string, Draft>>({});
  /** Password edits: new value / list, or null = remove the saved one. A key left out keeps the saved one. */
  const [secrets, setSecrets] = useState<Record<string, string | ListDraft | null>>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useSocketEvents(
    useMemo(
      () => ({
        [SocketRequests.PluginSettings]: (response: PluginSettingsResponse) => {
          if (response.pluginId !== plugin.id) return;
          setLoaded(response);
          setValues(response.values);
          setError(response.error);
        },
        [SocketRequests.PluginSettingsSave]: (response: PluginSettingsSaveResponse) => {
          if (response.pluginId !== plugin.id) return;
          setSaving(false);
          setError(response.error);
          if (response.error) return;
          notifications.show({ color: 'green', message: `${plugin.name} settings saved` });
          onClose();
        },
      }),
      [plugin.id, plugin.name, onClose]
    )
  );

  useEffect(() => {
    client.sendEvent<PluginSettingsRequest>(SocketRequests.PluginSettings, { pluginId: plugin.id });
  }, [client, plugin.id]);

  const setValue = (key: string, value: Draft) => setValues((v) => ({ ...v, [key]: value }));

  /** Emptying the input (or list) drops the edit: the saved password stays. */
  const setSecret = (key: string, value: string | ListDraft | null) =>
    setSecrets((s) =>
      value === null || value.length > 0
        ? { ...s, [key]: value }
        : Object.fromEntries(Object.entries(s).filter(([k]) => k !== key))
    );

  const save = (e: React.FormEvent) => {
    e.preventDefault();
    if (!loaded) return;
    const out: Record<string, PluginSettingValue> = {};
    for (const field of loaded.fields) {
      const draft = field.type === 'password' ? secrets[field.key] : values[field.key];
      if (draft === undefined) continue;
      out[field.key] = Array.isArray(draft) ? cleanList(field, draft) : draft;
      // A password list left with only blank entries keeps the saved one
      if (field.type === 'password' && Array.isArray(out[field.key]) && (out[field.key] as string[]).length === 0)
        delete out[field.key];
    }
    setSaving(true);
    setError(null);
    client.sendEvent<PluginSettingsSaveRequest>(SocketRequests.PluginSettingsSave, {
      pluginId: plugin.id,
      values: out,
    });
  };

  const renderField = (field: PluginSettingField) => {
    const common = {
      label: field.label,
      description: field.description ?? undefined,
      withAsterisk: field.required,
    };
    const value = values[field.key] ?? null;
    if (field.list && field.type !== 'password')
      return (
        <ListInput
          key={field.key}
          field={field}
          placeholder={field.placeholder ?? undefined}
          value={Array.isArray(value) ? value : []}
          onChange={(next) => setValue(field.key, next)}
        />
      );
    switch (field.type) {
      case 'password': {
        const saved = loaded?.secretsSet.includes(field.key) ?? false;
        const edit = secrets[field.key];
        const removing = edit === null;
        const hint = removing
          ? 'Removed when saved'
          : saved
            ? field.list
              ? 'Saved list is kept unless you add entries'
              : 'Saved, leave empty to keep'
            : (field.placeholder ?? undefined);
        return (
          <Stack gap={4} key={field.key}>
            {field.list ? (
              <ListInput
                field={{
                  ...field,
                  description: [field.description, saved || removing ? hint : null].filter(Boolean).join(' · ') || null,
                }}
                value={Array.isArray(edit) ? edit : []}
                onChange={(next) => setSecret(field.key, next)}
              />
            ) : (
              <PasswordInput
                {...common}
                autoComplete="new-password"
                placeholder={hint}
                value={typeof edit === 'string' ? edit : ''}
                onChange={(e) => setSecret(field.key, e.currentTarget.value)}
              />
            )}
            {saved && edit === undefined && (
              <Group justify="flex-end">
                <Button variant="subtle" size="compact-xs" color="red" onClick={() => setSecret(field.key, null)}>
                  Remove saved {field.label}
                </Button>
              </Group>
            )}
          </Stack>
        );
      }
      case 'number':
      case 'float':
        return (
          <NumberInput
            {...common}
            key={field.key}
            allowDecimal={field.type === 'float'}
            placeholder={field.placeholder ?? undefined}
            value={typeof value === 'number' ? value : ''}
            onChange={(next) => setValue(field.key, typeof next === 'number' ? next : null)}
          />
        );
      case 'boolean':
        return (
          <Switch
            key={field.key}
            label={field.label}
            description={field.description ?? undefined}
            checked={value === true}
            onChange={(e) => setValue(field.key, e.currentTarget.checked)}
          />
        );
      case 'select':
        return (
          <Select
            {...common}
            key={field.key}
            placeholder={field.placeholder ?? undefined}
            data={field.options ?? []}
            clearable={!field.required}
            value={typeof value === 'string' ? value : null}
            onChange={(next) => setValue(field.key, next)}
          />
        );
      default:
        return (
          <TextInput
            {...common}
            key={field.key}
            placeholder={field.placeholder ?? undefined}
            value={typeof value === 'string' ? value : ''}
            onChange={(e) => setValue(field.key, e.currentTarget.value)}
          />
        );
    }
  };

  return (
    <Modal opened onClose={onClose} title={`${plugin.name} settings`} size="md">
      <form onSubmit={save}>
        <Stack gap="sm">
          {error && (
            <Alert color="red" variant="light" style={{ overflowWrap: 'anywhere' }}>
              {error}
            </Alert>
          )}
          {loaded ? (
            groupFields(loaded.fields).map(([group, fields]) =>
              group === null ? (
                <React.Fragment key="">{fields.map(renderField)}</React.Fragment>
              ) : (
                <Fieldset key={group} legend={group} variant="filled">
                  <Stack gap="sm">{fields.map(renderField)}</Stack>
                </Fieldset>
              )
            )
          ) : (
            <Skeleton height={120} radius="md" />
          )}
          <Group justify="flex-end" gap="xs" mt="xs">
            <Button variant="default" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" loading={saving} disabled={!loaded || !!loaded.error}>
              Save
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
};

export default PluginSettingsDialog;
