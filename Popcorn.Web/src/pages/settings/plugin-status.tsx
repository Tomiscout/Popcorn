import React from 'react';
import { Alert, Stack, Text } from '@mantine/core';
import { IconAlertCircle, IconAlertTriangle, IconInfoCircle } from '@tabler/icons-react';
import { PluginStatusLevel, PluginStatusMessage } from '../../model/plugin.contract';

const color = (level: PluginStatusLevel) => (level === 'Error' ? 'red' : level === 'Warning' ? 'yellow' : 'blue');

const Icon: React.FC<{ level: PluginStatusLevel; size: number }> = ({ level, size }) =>
  level === 'Error' ? (
    <IconAlertCircle size={size} />
  ) : level === 'Warning' ? (
    <IconAlertTriangle size={size} />
  ) : (
    <IconInfoCircle size={size} />
  );

/** A plugin's `IPluginStatus` messages: one line each (plugin list row) or alerts (settings dialog). */
const PluginStatus: React.FC<{ status: PluginStatusMessage[]; variant: 'line' | 'alert' }> = ({ status, variant }) => {
  if (status.length === 0) return null;
  if (variant === 'alert')
    return (
      <Stack gap="xs">
        {status.map((s, i) => (
          <Alert
            key={i}
            color={color(s.level)}
            variant="light"
            icon={<Icon level={s.level} size={18} />}
            style={{ overflowWrap: 'anywhere', whiteSpace: 'pre-line' }}
          >
            {s.message}
          </Alert>
        ))}
      </Stack>
    );
  return (
    <>
      {status.map((s, i) => (
        <Text
          key={i}
          size="xs"
          c={s.level === 'Info' ? 'dimmed' : color(s.level)}
          style={{ display: 'flex', gap: 4, alignItems: 'flex-start', overflowWrap: 'anywhere' }}
        >
          <span style={{ flex: 'none', display: 'flex', paddingTop: 1 }}>
            <Icon level={s.level} size={14} />
          </span>
          {s.message}
        </Text>
      ))}
    </>
  );
};

export default PluginStatus;
