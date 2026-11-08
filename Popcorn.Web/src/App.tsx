import { IconDeviceTv, IconDownload, IconFolderOpen, IconSearch, IconSettings } from '@tabler/icons-react';

import './App.css';
import { Routes, Route, Link, Navigate, useLocation, BrowserRouter } from 'react-router-dom';
import { AppShell, Burger, Button, Group, MantineProvider, NavLink, Text } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { theme } from './theme';
import { Notifications } from '@mantine/notifications';
import WatchPage from './pages/watch/watch.page';
import SearchPage from './pages/search/search.page';
import FoldersPage from './pages/folders/folders.page';
import DownloadsPage from './pages/downloads/downloads.page';
import SettingsPage from './pages/settings/settings.page';
import ItemDetailsPage from './pages/item-details/item-details.page';
import { SocketRequests } from './socket/socket-requests';
import { lazy, Suspense, useEffect, useRef, useState } from 'react';
import { useWebSocket, WebSocketProvider } from './state/websocket-context';
import { FileProvider, useFileContext } from './state/file-context';
import { ModalsProvider } from '@mantine/modals';
import { initEvents, postMessage } from './utils/events';
import { disableDefaultListeners } from './player/Utlis/listener.utils';
import { usePlayerStore } from './state/player.store';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useAppStore } from './state/app.store';
import LoadingPage from './pages/loading/loading.page';
import SearchDetailsPage from './pages/search/search-details.page';
import { useSocketEvents } from './socket/useSocketEvents.hook';
import { useSocketBlobEvents } from './socket/useSocketBlobEvents.hook';
import { isDesktopApp, useIsMobile } from './utils/platform';
import { useSettingsSync } from './state/settings.store';
import { NetworkStats } from './pages/downloads/network-stats';
import { useDownloadAddedNotifications, useTorrentFileDrop } from './pages/downloads/downloads.hooks';

// Separate chunk, only fetched when a file is played in the browser (never in the desktop app).
const PopcornPlayer = lazy(() => import('./player/PopcornPlayer').then((m) => ({ default: m.PopcornPlayer })));

function AppContent() {
  const location = useLocation();
  const { client } = useWebSocket();
  const { addOrUpdateFileUrl } = useFileContext();
  const { fileId: playerFileId, isFloating, setIsFloating, addThumbnailFile } = usePlayerStore();
  const videoRef = useRef<HTMLVideoElement>(null);

  const [hasPageLoaded, setHasPageLoaded] = useState(false);
  const { isWebSocketConnecting } = useAppStore();
  useSettingsSync();
  useDownloadAddedNotifications();
  const isTorrentDragging = useTorrentFileDrop();
  const isMobile = useIsMobile();
  const [navOpened, { toggle: toggleNav, close: closeNav }] = useDisclosure(false);

  // Ignore hasPageLoaded for paths other than /watch
  useEffect(() => {
    if (!hasPageLoaded && location.pathname !== '/watch') {
      setHasPageLoaded(true);
    }
  }, [location.pathname, hasPageLoaded]);

  useSocketBlobEvents({
    [SocketRequests.ImageFile]: (payload: Uint8Array<ArrayBuffer>, metadata: string[]) => {
      const imageName = metadata[0];

      try {
        const imageFile = new File([new Uint8Array(payload)], imageName);

        const objectURL = URL.createObjectURL(imageFile);
        addOrUpdateFileUrl(imageName, objectURL);
      } catch (error) {
        console.error(`Error processing image blob for ${imageName}:`, error);
      }
    },

    [SocketRequests.ThumbnailFile]: (payload: Uint8Array<ArrayBuffer>, metadata: string[]) => {
      const imageName = metadata[0];

      try {
        const imageFile = new File([new Uint8Array(payload.buffer)], imageName);

        const objectURL = URL.createObjectURL(imageFile);

        console.log('Received thumbnail', imageName);
        addThumbnailFile(imageName, objectURL);
      } catch (error) {
        console.error(`Error processing image blob for ${imageName}:`, error);
      }
    },
  });

  useSocketEvents({
    [SocketRequests.ErrorWhenHandlingEvent]: (eventName: string) => {
      const message = `Failed to handle event on server: ${eventName}`;

      postMessage('player_error', message);
    },
    [SocketRequests.NextFile]: (fileId: string) => {
      if (isDesktopApp) {
        client.sendEvent(SocketRequests.FileOpen, fileId);
        return;
      }
      const { resetState, setFileId } = usePlayerStore.getState();
      resetState();
      setFileId(fileId);
    },
  });

  const toggleFloating = () => {
    setIsFloating(!isFloating);
  };

  if (isWebSocketConnecting) {
    return <LoadingPage />;
  }

  // Detail pages draw their backdrop header edge to edge.
  const noContentPadding = location.pathname.startsWith('/media-details') || location.pathname.startsWith('/watch/');
  const navLinks = [
    { to: '/watch', label: 'Watch', icon: IconDeviceTv },
    { to: '/media-search', label: 'Search', icon: IconSearch },
    { to: '/folders', label: 'Folders', icon: IconFolderOpen },
    { to: '/downloads', label: 'Downloads', icon: IconDownload },
    { to: '/settings', label: 'Settings', icon: IconSettings },
  ];
  return (
    <AppShell
      className="app-root"
      // Phones: a header with a menu button; the nav bar slides over the page.
      header={{ height: 50, collapsed: !isMobile }}
      navbar={{
        width: 180,
        breakpoint: 'sm',
        collapsed: { mobile: !navOpened },
      }}
      padding={noContentPadding ? 0 : { base: 'xs', sm: 'md' }}
    >
      <AppShell.Header bg="#201919" withBorder={false}>
        <Group h="100%" px="sm" gap="sm">
          <Burger opened={navOpened} onClick={toggleNav} size="sm" aria-label="Menu" />
          <Text fw={600}>{navLinks.find((l) => location.pathname.startsWith(l.to))?.label ?? 'Popcorn'}</Text>
        </Group>
      </AppShell.Header>

      {hasPageLoaded && (
        <AppShell.Navbar bg="#201919" withBorder={false}>
          {navLinks.map(({ to, label, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              component={Link}
              active={location.pathname === to}
              label={label}
              leftSection={<Icon size={24} stroke={1.5} />}
              onClick={closeNav}
            />
          ))}

          {!isDesktopApp && (
            <Button variant="light" fullWidth mt="md" onClick={toggleFloating}>
              {isFloating ? 'Dock Player' : 'Float Player'}
            </Button>
          )}

          <NetworkStats />
        </AppShell.Navbar>
      )}

      {!isDesktopApp && playerFileId && (
        <Suspense fallback={null}>
          <PopcornPlayer videoRef={videoRef} />
        </Suspense>
      )}

      <AppShell.Main className="app-content">
        <Routes>
          <Route path="/" element={<Navigate to={'/watch'} replace />} />
          <Route path="/watch" element={<WatchPage pageLoaded={() => setHasPageLoaded(true)} />} />
          <Route path="/watch/:id" element={<ItemDetailsPage />} />
          <Route path="/media-search" element={<SearchPage />} />
          <Route path="/media-details/:pluginId/:id" element={<SearchDetailsPage />} />
          <Route path="/folders" element={<FoldersPage />} />
          <Route path="/downloads" element={<DownloadsPage />} />
          <Route path="/torrent-client" element={<Navigate to="/downloads" replace />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="*" element={<Navigate to="/watch" replace />} />
        </Routes>
      </AppShell.Main>

      {isTorrentDragging && (
        <div className="torrent-drop-overlay">
          <IconDownload size={48} stroke={1.5} />
          Drop .torrent files to download
        </div>
      )}
    </AppShell>
  );
}

function App() {
  disableDefaultListeners();
  initEvents();
  const queryClient = new QueryClient();

  return (
    <QueryClientProvider client={queryClient}>
      <FileProvider>
        <BrowserRouter>
          <MantineProvider theme={theme} forceColorScheme="dark">
            <Notifications />
            <ModalsProvider>
              <WebSocketProvider>
                <AppContent />
              </WebSocketProvider>
            </ModalsProvider>
          </MantineProvider>
        </BrowserRouter>
      </FileProvider>
    </QueryClientProvider>
  );
}

export default App;
