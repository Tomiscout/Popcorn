import { createRoot } from 'react-dom/client';
import { MantineProvider } from '@mantine/core';
import '@mantine/core/styles.css';
import { theme } from '../../theme';
import { initEvents } from '../../utils/events';
import { NativePlayer } from './NativePlayer';

// Entry of player.html: the overlay of the native players (Popcorn.Player.Linux CEF, Popcorn.Player.Windows WebView2).
initEvents();

createRoot(document.getElementById('root')!).render(
  <MantineProvider theme={theme} forceColorScheme="dark">
    <NativePlayer />
  </MantineProvider>
);
