import { useMediaQuery } from '@mantine/hooks';

/**
 * True when the UI runs inside the Popcorn desktop shell (Popcorn.Linux, CEF), which appends
 * `PopcornDesktop/<version>` to its User-Agent. The shell has no built-in web player: files are
 * opened through the server (`FileOpen` → native player / default app) and the player chunk is
 * never loaded.
 */
export const isDesktopApp = /\bPopcornDesktop\//.test(navigator.userAgent);

/** Phone-sized screen: below Mantine's `sm` breakpoint, where AppShell collapses the nav bar. */
export const MOBILE_QUERY = '(max-width: 47.99em)';

export const useIsMobile = () => useMediaQuery(MOBILE_QUERY, window.matchMedia(MOBILE_QUERY).matches);

/** Touch screen as the main input: phones and tablets. */
export const isTouchDevice = window.matchMedia('(pointer: coarse)').matches;
