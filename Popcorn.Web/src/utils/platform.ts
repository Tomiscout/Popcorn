/**
 * True when the UI runs inside the Popcorn desktop shell (Popcorn.Linux, CEF), which appends
 * `PopcornDesktop/<version>` to its User-Agent. The shell has no built-in web player: files are
 * opened through the server (`FileOpen` → native player / default app) and the player chunk is
 * never loaded.
 */
export const isDesktopApp = /\bPopcornDesktop\//.test(navigator.userAgent);
