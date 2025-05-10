import { Modal, TextInput, Button, Image, FileInput, Flex } from '@mantine/core';
import { useState, useEffect } from 'react';
import { MediaScanResult } from '../../model/folders.contract';

interface AddToLibraryModalProps {
  opened: boolean;
  onClose(): void;
  scanResult: MediaScanResult;
  onConfirm(rootPath: string, title: string, imageFile?: File): void;
}

const defaultImage = '/images/default-media.jpg';

export function AddToLibraryModal({ opened, onClose, scanResult, onConfirm }: AddToLibraryModalProps) {
  const [title, setTitle] = useState('');
  const [imageUrl, setImageUrl] = useState<string>(defaultImage);
  const [imageFile, setImageFile] = useState<File>();

  useEffect(() => {
    if (!scanResult) return;

    setTitle(scanResult.title);
    setImageUrl(defaultImage);
  }, [scanResult]);

  const handleImageChange = (file: File | null) => {
    if (file) {
      setImageFile(file);
      setImageUrl(URL.createObjectURL(file));
    }
  };

  const handleConfirm = () => {
    const sanitizedTitle = title.trim().replace(/;/g, ''); //Need to remove ; because it's a separator. Sorry Steins;Gate
    if (!sanitizedTitle) return;
    onConfirm(scanResult.folderPath, title, imageFile);
    onClose();
  };

  return (
    <Modal opened={opened} onClose={onClose} size="lg" centered>
      <Modal.Title>{'Add to Library'}</Modal.Title>

      <TextInput mt="md" label="Media title" value={title} onChange={(e) => setTitle(e.currentTarget.value)} mb="md" />

      <Flex direction="column" justify="space-between">
        <Image src={imageUrl} maw={180} mah={300} alt="Selected media" />
        <FileInput accept="image/*" placeholder="Select image" onChange={handleImageChange} mb="md" />

        <Flex direction="column" justify="flex-end">
          <Button fullWidth onClick={handleConfirm}>
            Confirm
          </Button>
        </Flex>
      </Flex>
    </Modal>
  );
}
