import { createTheme } from '@mantine/core';

/** Mantine theme shared by the main UI and the native player overlay (player.html). */
export const theme = createTheme({
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
