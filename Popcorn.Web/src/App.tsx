import { IconDeviceTv, IconDownload, IconFolderOpen, IconSearch, IconSettings } from '@tabler/icons-react';

import './App.css';
import { Routes, Route, Link, Navigate, useLocation, BrowserRouter } from 'react-router-dom';
import { AppShell, createTheme, MantineProvider, NavLink } from '@mantine/core';
import WatchPage from './pages/watch/watch.page';
import MediaSearchPage from './pages/search/media-search.page';
import TorrentSearchPage from './pages/search/torrent-search.page';
import FoldersPage from './pages/folders/folders.page';
import DownloadsPage from './pages/downloads/downloads.page';
import SettingsPage from './pages/settings/settings.page';
import ItemDetailsPage from './pages/item-details/item-details.page';
import { SocketRequests } from './socket/socket-requests';
import { useEffect, useState } from 'react';
import { useWebSocket, WebSocketProvider } from './state/websocket-context';
import { FileProvider, useFileContext } from './state/file-context';
import { parseFilenames } from './utils/tags.parser';
import { ModalsProvider } from '@mantine/modals';

const theme = createTheme({
  primaryColor: 'primaryRed',
  colors: {
    primaryRed: [
      '#f0d7d7',
      '#e9c3c3',
      '#e1afaf',
      '#da9b9b',
      '#d28686',
      '#cb7272',
      '#c35e5e',
      '#bc4a4a',
      '#b43636', //Main
      '#a23131',
    ],
  },
});

function AppContent() {
  const location = useLocation();
  const { client, isClientReady } = useWebSocket();
  const { addOrUpdateFileUrl } = useFileContext();

  const [hasPageLoaded, setHasPageLoaded] = useState(false);

  // Ignore hasPageLoaded for paths other than /watch
  useEffect(() => {
    if (!hasPageLoaded && location.pathname !== '/watch') {
      setHasPageLoaded(true);
    }
  }, [location.pathname, hasPageLoaded]);

  // Global image handler
  useEffect(() => {
    const imageHandler = (payload: any, metadata: string[]) => {
      const imageName = metadata[0];
      try {
        const imageFile = new File([payload as Uint8Array], imageName);
        const objectURL = URL.createObjectURL(imageFile);
        addOrUpdateFileUrl(imageName, objectURL);
      } catch (error) {
        console.error(`Error processing image blob for ${imageName}:`, error);
      }
    };

    const tagsHandler = (fileNames: Record<string, string>) => {
      const tags = parseFilenames(fileNames);
      client.sendEvent(SocketRequests.ParseTags, tags);
    };

    client.onBlobEvent(SocketRequests.ImageFile, imageHandler);
    client.onEvent(SocketRequests.ParseTags, tagsHandler);

    return () => {
      client.onBlobEventUnregiser(SocketRequests.ImageFile);
      client.onEventUnregiser(SocketRequests.ParseTags);
    };
  }, [isClientReady]);

  return (
    <AppShell
      className="app-root"
      navbar={{
        width: 180,
        breakpoint: 'off',
      }}
      padding="md"
    >
      {hasPageLoaded && (
        <AppShell.Navbar bg="#201919" withBorder={false}>
          <NavLink
            to="/watch"
            component={Link}
            active={location.pathname === '/watch'}
            label="Watch"
            leftSection={<IconDeviceTv size={24} stroke={1.5} />}
          />
          <NavLink
            to="/media-search"
            component={Link}
            active={location.pathname === '/media-search'}
            label="Media search"
            leftSection={<IconSearch size={24} stroke={1.5} />}
          />
          <NavLink
            to="/torrent-search"
            component={Link}
            active={location.pathname === '/torrent-search'}
            label="Torrent search"
            leftSection={<IconSearch size={24} stroke={1.5} />}
          />
          <NavLink
            to="/folders"
            component={Link}
            active={location.pathname === '/folders'}
            label="Folders"
            leftSection={<IconFolderOpen size={24} stroke={1.5} />}
          />
          <NavLink
            to="/torrent-client"
            component={Link}
            active={location.pathname === '/torrent-client'}
            label="Downloads"
            leftSection={<IconDownload size={24} stroke={1.5} />}
          />
          <NavLink
            to="/settings"
            component={Link}
            active={location.pathname === '/settings'}
            label="Settings"
            leftSection={<IconSettings size={24} stroke={1.5} />}
          />
        </AppShell.Navbar>
      )}

      <AppShell.Main className="app-content">
        <Routes>
          <Route path="/" element={<Navigate to={'/watch'} replace />} />
          <Route path="/watch" element={<WatchPage pageLoaded={() => setHasPageLoaded(true)} />} />
          <Route path="/watch/:id" element={<ItemDetailsPage />} />
          <Route path="/media-search" element={<MediaSearchPage />} />
          <Route path="/torrent-search" element={<TorrentSearchPage />} />
          <Route path="/folders" element={<FoldersPage />} />
          <Route path="/torrent-client" element={<DownloadsPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="*" element={<Navigate to="/watch" replace />} />
        </Routes>
      </AppShell.Main>
    </AppShell>
  );
}

function App() {
  return (
    <FileProvider>
      <BrowserRouter>
        <MantineProvider theme={theme} forceColorScheme="dark">
          <ModalsProvider>
            <WebSocketProvider>
              <AppContent />
            </WebSocketProvider>
          </ModalsProvider>
        </MantineProvider>
      </BrowserRouter>
    </FileProvider>
  );
}

export default App;
