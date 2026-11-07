import { Modal, TextInput, Button, Stack, Divider } from '@mantine/core';
import { useState } from 'react';
import { MediaScanResult } from '../../model/folders.contract';
import { CreateMediaItemRequest } from '../../model/media.contract';
import { MediaImagePicker } from '../../components/image-picker/media-image-picker';

interface AddToLibraryModalProps {
  opened: boolean;
  onClose(): void;
  scanResult: MediaScanResult | null;
  onConfirm(request: CreateMediaItemRequest): void;
}

export function AddToLibraryModal({ opened, onClose, scanResult, onConfirm }: AddToLibraryModalProps) {
  return (
    <Modal opened={opened} onClose={onClose} size="xl" centered title="Add to Library">
      {/* Keyed by folder: a new folder starts with fresh title, picks and searches. */}
      {scanResult && (
        <AddToLibraryForm key={scanResult.folderPath} scanResult={scanResult} onClose={onClose} onConfirm={onConfirm} />
      )}
    </Modal>
  );
}

function AddToLibraryForm({
  scanResult,
  onClose,
  onConfirm,
}: {
  scanResult: MediaScanResult;
  onClose(): void;
  onConfirm(request: CreateMediaItemRequest): void;
}) {
  const [title, setTitle] = useState(scanResult.title);
  const [posterUrl, setPosterUrl] = useState<string | null>(null);
  const [backdropUrl, setBackdropUrl] = useState<string | null>(null);

  const handleConfirm = () => {
    const sanitizedTitle = title.trim();
    if (!sanitizedTitle) return;
    onConfirm({ folderPath: scanResult.folderPath, title: sanitizedTitle, posterUrl, backdropUrl });
    onClose();
  };

  return (
    <Stack gap="md">
      <TextInput label="Media title" value={title} onChange={(e) => setTitle(e.currentTarget.value)} />
      <MediaImagePicker kind="Poster" name={title} value={posterUrl} onChange={setPosterUrl} autoSelect />
      <Divider />
      <MediaImagePicker kind="Backdrop" name={title} value={backdropUrl} onChange={setBackdropUrl} autoSelect />
      <Button fullWidth onClick={handleConfirm} disabled={!title.trim()}>
        Confirm
      </Button>
    </Stack>
  );
}
