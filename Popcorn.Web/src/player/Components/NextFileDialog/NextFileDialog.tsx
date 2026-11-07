import React, { useEffect, useState } from 'react';
import { Button, CloseButton, Group, Text, Title } from '@mantine/core';
import { postMessage } from '../../../utils/events';
import { usePlayerStore } from '../../../state/player.store';
import './NextFileDialog.css';

/** The next episode isn't there. Closing the dialog leaves everything as it is. */
export const NextFileDialog: React.FC = () => {
  const plan = usePlayerStore((s) => s.nextFileWarning);
  const close = () => usePlayerStore.getState().setNextFileWarning(null);
  const [refreshing, setRefreshing] = useState(false);

  // Every answer to a refresh replaces the plan (or closes the dialog).
  useEffect(() => setRefreshing(false), [plan]);

  if (!plan) return null;

  const refresh = () => {
    setRefreshing(true);
    postMessage('next_file_refresh');
  };

  const findFile = () => {
    const query = `${plan.itemTitle ?? ''} ${plan.missingLabel}`.trim();
    postMessage('next_file_find', `/watch/${plan.itemId}?tab=sources&q=${encodeURIComponent(query)}`);
    close();
  };

  const playNext = () => {
    postMessage('play_file', plan.nextFileId);
    close();
  };

  return (
    // Player shortcuts listen for keyup on the document, and clicks must not reach the video behind.
    <div
      className="next-file-dialog"
      role="alertdialog"
      aria-label={`${plan.missingLabel} is missing`}
      onKeyUp={(e) => e.stopPropagation()}
      onClick={(e) => e.stopPropagation()}
      onDoubleClick={(e) => e.stopPropagation()}
    >
      <Group justify="space-between" align="flex-start" wrap="nowrap" mb="xs">
        <Title order={4}>{plan.missingLabel} is missing</Title>
        <CloseButton aria-label="Close" title="Close" onClick={close} />
      </Group>
      <Text size="sm" c="dimmed" mb="md">
        The next file in {plan.itemTitle ?? 'this item'} is {plan.nextLabel}.
      </Text>
      <Group gap="xs">
        <Button variant="default" size="xs" loading={refreshing} onClick={refresh}>
          Refresh files
        </Button>
        {plan.itemId && (
          <Button variant="default" size="xs" onClick={findFile}>
            Find file
          </Button>
        )}
        {plan.nextFileId && (
          <Button size="xs" onClick={playNext}>
            Play {plan.nextLabel} next
          </Button>
        )}
      </Group>
    </div>
  );
};
