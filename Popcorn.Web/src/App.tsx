import IconDeviceTv from '@tabler/icons-react/dist/esm/icons/IconDeviceTv';
import IconDownload from '@tabler/icons-react/dist/esm/icons/IconDownload';
import IconFolderOpen from '@tabler/icons-react/dist/esm/icons/IconFolderOpen';
import IconSearch from '@tabler/icons-react/dist/esm/icons/IconSearch';
import IconSettings from '@tabler/icons-react/dist/esm/icons/IconSettings';

import './App.css';
import { BrowserRouter, Routes, Route, Link, Navigate, useLocation } from 'react-router-dom';
import { AppShell, createTheme, MantineProvider, NavLink } from '@mantine/core';
import WatchPage from './pages/watch/watch.page';
import MediaSearchPage from './pages/search/media-search.page';
import TorrentSearchPage from './pages/search/torrent-search.page';
import FoldersPage from './pages/folders/folders.page';
import DownloadsPage from './pages/downloads/downloads.page';
import SettingsPage from './pages/settings/settings.page';
import ItemDetailsPage from './pages/item-details/item-details.page';
import { WebSocketProvider } from './socket/websocket-context';

const theme = createTheme({
  primaryColor: 'red',
});

function AppContent() {
  const location = useLocation();

  return (
    <AppShell
      navbar={{
        width: 180,
        breakpoint: 'off',
      }}
      padding="md"
    >
      <AppShell.Navbar>
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

      <AppShell.Main>
        <Routes>
          <Route path="/" element={<Navigate to={'/watch'} replace />} />
          <Route path="/watch" element={<WatchPage />} />
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
    <BrowserRouter>
      <MantineProvider theme={theme} forceColorScheme="dark">
        <WebSocketProvider>
          <AppContent />
        </WebSocketProvider>
      </MantineProvider>
    </BrowserRouter>
  );
}

export default App;
