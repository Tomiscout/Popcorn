//! CEF application: one Views window hosting one browser pointed at Popcorn.Server.
//!
//! Threading: all CEF callbacks below run on the CEF UI thread (the main thread, since we use
//! `run_message_loop`). UI-only state lives in a thread-local; the background server watcher
//! talks to the UI thread through atomics + posted tasks.

use std::cell::RefCell;
use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};

use cef::*;

use crate::log::{error, info, warning};
use crate::server::{self, Target};

const TITLE: &str = "Popcorn";
/// Wayland app_id / X11 WM_CLASS. Matches `popcorn.desktop`, so the compositor picks up its icon.
const APP_ID: &str = "popcorn";
/// Same as the web UI's `<body>` background, avoids a white flash before first paint.
const BACKGROUND_ARGB: u32 = 0xFF24_2424;
const ICON_PNG: &[u8] = include_bytes!("../popcorn.png");
const DEFAULT_SIZE: Size = Size {
    width: 1280,
    height: 720,
};
const MIN_SIZE: Size = Size {
    width: 480,
    height: 320,
};

static TARGET: OnceLock<Target> = OnceLock::new();
static STARTED: OnceLock<Instant> = OnceLock::new();
/// The server answered a TCP connect; the browser may navigate to it.
static SERVER_READY: AtomicBool = AtomicBool::new(false);
/// `on_context_initialized` ran, so `post_task` to the UI thread works.
static CEF_READY: AtomicBool = AtomicBool::new(false);
static WATCHER_RUNNING: AtomicBool = AtomicBool::new(false);
/// Last status line shown on the waiting page.
static STATUS: Mutex<String> = Mutex::new(String::new());

thread_local! {
    static UI: RefCell<UiState> = RefCell::new(UiState::default());
}

#[derive(Default)]
struct UiState {
    window: Option<Window>,
    browser: Option<Browser>,
    /// The main frame currently shows (or is about to show) the waiting page.
    showing_wait_page: bool,
    app_loaded_once: bool,
}

fn target() -> &'static Target {
    TARGET.get().expect("server target not set")
}

fn elapsed_ms() -> u128 {
    STARTED.get().map_or(0, |s| s.elapsed().as_millis())
}

/// Initialises CEF, runs the message loop until the window closes. Returns the process exit code.
pub fn run(args: &args::Args, target: Target, data_dir: &Path, started: Instant) -> i32 {
    let _ = STARTED.set(started);

    // Usually sub-millisecond (connection refused is immediate on localhost), so it's fine to
    // do synchronously and lets us open the app URL directly in the common case.
    if target.is_reachable() {
        info!("Server reachable at {}", target.url);
        SERVER_READY.store(true, Ordering::SeqCst);
    } else {
        info!("Server not reachable at {}", target.url);
    }
    let start_server = !target.explicit;
    let _ = TARGET.set(target);

    let cef_dir = data_dir.join("cef");
    let cef_dir = cef_dir.to_string_lossy();
    let cef_log = data_dir.join("logs").join("cef.log");
    let settings = Settings {
        // Persistent profile (localStorage, cookies). Also acts as the process-singleton key:
        // a second launch hands over to `on_already_running_app_relaunch` in the first one.
        root_cache_path: CefString::from(cef_dir.as_ref()),
        cache_path: CefString::from(cef_dir.as_ref()),
        persist_session_cookies: 1,
        log_file: CefString::from(cef_log.to_string_lossy().as_ref()),
        log_severity: LogSeverity::WARNING,
        background_color: BACKGROUND_ARGB,
        // The web UI checks for `PopcornDesktop/` to drop the built-in web player (utils/platform.ts).
        user_agent_product: CefString::from(user_agent_product().as_str()),
        no_sandbox: std::env::var_os("POPCORN_NO_SANDBOX").is_some() as _,
        ..Default::default()
    };

    let mut app = PopcornApp::new();
    if initialize(
        Some(args.as_main_args()),
        Some(&settings),
        Some(&mut app),
        std::ptr::null_mut(),
    ) != 1
    {
        let code = get_exit_code();
        // Either another instance owns the profile (it has been asked to focus its window) or CEF
        // failed to start (details in cef.log).
        info!(
            "CEF did not initialise (exit code {code}): another instance is running or start-up failed"
        );
        return code;
    }

    // Only the instance that owns the profile starts the server (a second launch exits above and
    // the first one retries in `on_already_running_app_relaunch`).
    if !SERVER_READY.load(Ordering::SeqCst) {
        spawn_server_watcher(start_server);
    }

    run_message_loop();
    shutdown();
    0
}

/// Product part of the User-Agent; replaces CEF's default `Chrome/<version>`, so keep that first.
fn user_agent_product() -> String {
    format!(
        "Chrome/{}.{}.{}.{} PopcornDesktop/{}",
        sys::CHROME_VERSION_MAJOR,
        sys::CHROME_VERSION_MINOR,
        sys::CHROME_VERSION_BUILD,
        sys::CHROME_VERSION_PATCH,
        env!("CARGO_PKG_VERSION")
    )
}

// ---------------------------------------------------------------------------------------------
// App / browser process
// ---------------------------------------------------------------------------------------------

wrap_app! {
    struct PopcornApp;

    impl App {
        fn on_before_command_line_processing(
            &self,
            process_type: Option<&CefString>,
            command_line: Option<&mut CommandLine>,
        ) {
            let is_browser = process_type.is_none_or(|t| t.to_string().is_empty());
            let (true, Some(cmd)) = (is_browser, command_line) else {
                return;
            };

            // Chromium extras a single-page local app doesn't need (less start-up work and
            // no background network traffic / keyring prompts).
            for switch in [
                "no-first-run",
                "no-default-browser-check",
                "disable-background-networking",
                "disable-component-update",
                "disable-default-apps",
                "disable-sync",
            ] {
                cmd.append_switch(Some(&CefString::from(switch)));
            }
            set_switch(cmd, "autoplay-policy", "no-user-gesture-required");
            // Don't touch KWallet / gnome-keyring.
            set_switch(cmd, "password-store", "basic");
            // NetworkTimeServiceQuerying: stops clients2.google.com/time requests.
            append_list_switch(cmd, "disable-features", "Translate,MediaRouter,OptimizationHints,NetworkTimeServiceQuerying");

            if let Some(platform) = ozone_platform() {
                set_switch(cmd, "ozone-platform", platform.as_str());
            }
        }

        fn browser_process_handler(&self) -> Option<BrowserProcessHandler> {
            Some(PopcornBrowserProcessHandler::new())
        }
    }
}

/// Chromium's own detection relies on `XDG_SESSION_TYPE`, which is missing when we're started
/// from a systemd user service (tray "Open app"), so decide from the display sockets instead.
/// `POPCORN_OZONE=x11|wayland` overrides.
fn ozone_platform() -> Option<String> {
    let non_empty = |name: &str| std::env::var(name).ok().filter(|v| !v.is_empty());
    non_empty("POPCORN_OZONE")
        .or_else(|| non_empty("WAYLAND_DISPLAY").map(|_| "wayland".to_owned()))
        .or_else(|| non_empty("DISPLAY").map(|_| "x11".to_owned()))
}

fn set_switch(cmd: &mut CommandLine, name: &str, value: &str) {
    cmd.append_switch_with_value(Some(&CefString::from(name)), Some(&CefString::from(value)));
}

/// Adds to a comma-separated switch without dropping values already set by CEF.
fn append_list_switch(cmd: &mut CommandLine, name: &str, values: &str) {
    let key = CefString::from(name);
    let existing = CefString::from(&cmd.switch_value(Some(&key))).to_string();
    let merged = if existing.is_empty() {
        values.to_owned()
    } else {
        format!("{existing},{values}")
    };
    set_switch(cmd, name, &merged);
}

wrap_browser_process_handler! {
    struct PopcornBrowserProcessHandler;

    impl BrowserProcessHandler {
        fn on_context_initialized(&self) {
            CEF_READY.store(true, Ordering::SeqCst);
            info!("CEF initialised after {} ms", elapsed_ms());
            create_main_window();
        }

        fn on_already_running_app_relaunch(
            &self,
            _command_line: Option<&mut CommandLine>,
            _current_directory: Option<&CefString>,
        ) -> ::std::os::raw::c_int {
            info!("Second launch detected, focusing existing window");
            focus_main_window();
            // Relaunching while stuck on the waiting page = "try again".
            if !SERVER_READY.load(Ordering::SeqCst) {
                spawn_server_watcher(!target().explicit);
            }
            1
        }
    }
}

fn create_main_window() {
    let url = if SERVER_READY.load(Ordering::SeqCst) {
        target().app_url()
    } else {
        UI.with(|ui| ui.borrow_mut().showing_wait_page = true);
        wait_page_url()
    };

    let mut client = PopcornClient::new();
    let mut view_delegate = PopcornBrowserViewDelegate::new();
    let settings = BrowserSettings {
        background_color: BACKGROUND_ARGB,
        ..Default::default()
    };
    let browser_view = browser_view_create(
        Some(&mut client),
        Some(&CefString::from(url.as_str())),
        Some(&settings),
        None,
        None,
        Some(&mut view_delegate),
    );
    if browser_view.is_none() {
        error!("Failed to create browser view");
        quit_message_loop();
        return;
    }

    let mut window_delegate = PopcornWindowDelegate::new(RefCell::new(browser_view), true);
    window_create_top_level(Some(&mut window_delegate));
}

fn set_window_icon(window: &Window) {
    if let Some(mut icon) = image_create()
        && icon.add_png(1.0, Some(ICON_PNG)) == 1
    {
        window.set_window_icon(Some(&mut icon));
        window.set_window_app_icon(Some(&mut icon));
    }
}

fn window_for(browser: Option<&mut Browser>) -> Option<Window> {
    browser_view_get_for_browser(browser)?.window()
}

fn focus_main_window() {
    let window = UI.with(|ui| ui.borrow().window.clone());
    if let Some(window) = window {
        if window.is_minimized() == 1 {
            window.restore();
        }
        window.show();
        window.activate();
    }
}

// ---------------------------------------------------------------------------------------------
// Window (CEF Views)
// ---------------------------------------------------------------------------------------------

wrap_window_delegate! {
    struct PopcornWindowDelegate {
        browser_view: RefCell<Option<BrowserView>>,
        // false for DevTools windows.
        is_main: bool,
    }

    impl ViewDelegate {
        fn preferred_size(&self, _view: Option<&mut View>) -> Size {
            DEFAULT_SIZE
        }

        fn minimum_size(&self, _view: Option<&mut View>) -> Size {
            MIN_SIZE
        }
    }

    impl PanelDelegate {}

    impl WindowDelegate {
        fn on_window_created(&self, window: Option<&mut Window>) {
            let Some(window) = window else { return };

            if let Some(browser_view) = self.browser_view.borrow().as_ref() {
                let mut view = View::from(browser_view);
                window.add_child_view(Some(&mut view));
            }

            let title = if self.is_main { TITLE } else { "Popcorn DevTools" };
            window.set_title(Some(&CefString::from(title)));
            set_window_icon(window);

            window.show();
            if let Some(browser_view) = self.browser_view.borrow().as_ref() {
                browser_view.request_focus();
            }

            if self.is_main {
                UI.with(|ui| ui.borrow_mut().window = Some(window.clone()));
                info!("Window shown after {} ms", elapsed_ms());
            }
        }

        fn on_window_destroyed(&self, _window: Option<&mut Window>) {
            *self.browser_view.borrow_mut() = None;
            if self.is_main {
                UI.with(|ui| ui.borrow_mut().window = None);
            }
        }

        fn can_resize(&self, _window: Option<&mut Window>) -> ::std::os::raw::c_int {
            1
        }

        fn can_maximize(&self, _window: Option<&mut Window>) -> ::std::os::raw::c_int {
            1
        }

        fn can_minimize(&self, _window: Option<&mut Window>) -> ::std::os::raw::c_int {
            1
        }

        fn can_close(&self, _window: Option<&mut Window>) -> ::std::os::raw::c_int {
            // Let the page run its unload handlers first; CEF closes the window afterwards.
            let browser = self.browser_view.borrow().as_ref().and_then(|bv| bv.browser());
            match browser.and_then(|b| b.host()) {
                Some(host) => host.try_close_browser(),
                None => 1,
            }
        }

        fn linux_window_properties(
            &self,
            _window: Option<&mut Window>,
            properties: Option<&mut LinuxWindowProperties>,
        ) -> ::std::os::raw::c_int {
            let Some(properties) = properties else { return 0 };
            properties.wayland_app_id = out_param_string(APP_ID);
            properties.wm_class_class = out_param_string(APP_ID);
            properties.wm_class_name = out_param_string(APP_ID);
            1
        }
    }
}

/// A string for a CEF struct out-parameter. cef-rs 156.3 converts an *owned* `CefString` back to
/// the C struct as an empty string, so hand it a CEF-allocated string (wrapped as "borrowed",
/// which passes through unchanged; CEF frees it with the struct).
fn out_param_string(value: &str) -> CefString {
    let utf16: Vec<u16> = value.encode_utf16().collect();
    // SAFETY: zeroed cef_string_t is the documented empty value; copy=1 makes CEF allocate
    // its own buffer (with dtor), so `utf16` may be dropped afterwards.
    let raw = unsafe {
        let mut raw: sys::cef_string_utf16_t = std::mem::zeroed();
        sys::cef_string_utf16_set(utf16.as_ptr(), utf16.len(), &mut raw, 1);
        raw
    };
    CefString::from(raw)
}

wrap_browser_view_delegate! {
    struct PopcornBrowserViewDelegate {}

    impl ViewDelegate {}

    impl BrowserViewDelegate {
        /// Alloy style: a plain web view. Chrome style would bring Chrome browser behaviour along
        /// (session restore of old URLs, popups as tabbed Chrome windows, ...). The Window itself
        /// stays Chrome style (allowed to host Alloy views) because only Chrome-style windows
        /// honour `linux_window_properties` (Wayland app_id -> desktop file + icon).
        fn browser_runtime_style(&self) -> RuntimeStyle {
            RuntimeStyle::ALLOY
        }

        fn on_popup_browser_view_created(
            &self,
            _browser_view: Option<&mut BrowserView>,
            popup_browser_view: Option<&mut BrowserView>,
            _is_devtools: ::std::os::raw::c_int,
        ) -> ::std::os::raw::c_int {
            // Only DevTools gets here; page popups are cancelled in `on_before_popup`.
            let mut delegate = PopcornWindowDelegate::new(RefCell::new(popup_browser_view.cloned()), false);
            window_create_top_level(Some(&mut delegate));
            1
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Browser client
// ---------------------------------------------------------------------------------------------

wrap_client! {
    struct PopcornClient;

    impl Client {
        fn life_span_handler(&self) -> Option<LifeSpanHandler> {
            Some(PopcornLifeSpanHandler::new())
        }

        fn load_handler(&self) -> Option<LoadHandler> {
            Some(PopcornLoadHandler::new())
        }

        fn keyboard_handler(&self) -> Option<KeyboardHandler> {
            Some(PopcornKeyboardHandler::new())
        }

        fn display_handler(&self) -> Option<DisplayHandler> {
            Some(PopcornDisplayHandler::new())
        }
    }
}

wrap_display_handler! {
    struct PopcornDisplayHandler;

    impl DisplayHandler {
        fn on_fullscreen_mode_change(&self, browser: Option<&mut Browser>, fullscreen: ::std::os::raw::c_int) {
            // HTML5 fullscreen (video player) -> fullscreen the whole window.
            if let Some(window) = window_for(browser)
                && window.is_fullscreen() != fullscreen
            {
                window.set_fullscreen(fullscreen);
            }
        }
    }
}

// Windows virtual key codes (CEF uses them on every platform) and `cef_event_flags_t` bits.
const CTRL_SHIFT: u32 = CONTROL | SHIFT;
const VK_F5: i32 = 0x74;
const VK_F11: i32 = 0x7A;
const VK_F12: i32 = 0x7B;
const VK_I: i32 = 0x49;
const VK_R: i32 = 0x52;
const SHIFT: u32 = 1 << 1;
const CONTROL: u32 = 1 << 2;
const ALT: u32 = 1 << 3;

wrap_keyboard_handler! {
    struct PopcornKeyboardHandler;

    impl KeyboardHandler {
        /// Alloy style has no browser shortcuts, so provide the few that are useful in an app.
        fn on_pre_key_event(
            &self,
            browser: Option<&mut Browser>,
            event: Option<&KeyEvent>,
            _os_event: Option<&mut sys::XEvent>,
            _is_keyboard_shortcut: Option<&mut ::std::os::raw::c_int>,
        ) -> ::std::os::raw::c_int {
            let (Some(browser), Some(event)) = (browser, event) else { return 0 };
            if event.type_ != KeyEventType::RAWKEYDOWN {
                return 0;
            }
            let mods = event.modifiers & (SHIFT | CONTROL | ALT);
            match (event.windows_key_code, mods) {
                (VK_F12, 0) | (VK_I, CTRL_SHIFT) => {
                    if let Some(host) = browser.host() {
                        host.show_dev_tools(Some(&WindowInfo::default()), None, None, None);
                    }
                }
                (VK_F5, 0) | (VK_R, CONTROL) => browser.reload(),
                (VK_F5, CONTROL) | (VK_R, CTRL_SHIFT) => browser.reload_ignore_cache(),
                (VK_F11, 0) => {
                    if let Some(window) = window_for(Some(browser)) {
                        window.set_fullscreen((window.is_fullscreen() == 0) as _);
                    }
                }
                _ => return 0,
            }
            1
        }
    }
}

wrap_life_span_handler! {
    struct PopcornLifeSpanHandler;

    impl LifeSpanHandler {
        fn on_after_created(&self, browser: Option<&mut Browser>) {
            let Some(browser) = browser else { return };
            UI.with(|ui| {
                let mut ui = ui.borrow_mut();
                if ui.browser.is_none() {
                    ui.browser = Some(browser.clone());
                }
            });
            // The server may have come up while the browser was being created.
            sync_navigation();
        }

        fn on_before_close(&self, browser: Option<&mut Browser>) {
            let Some(browser) = browser else { return };
            let is_main = UI.with(|ui| {
                let mut ui = ui.borrow_mut();
                let is_main = ui.browser.as_ref().is_some_and(|b| b.is_same(Some(&mut browser.clone())) == 1);
                if is_main {
                    ui.browser = None;
                }
                is_main
            });
            if is_main {
                info!("Main browser closed, quitting");
                quit_message_loop();
            }
        }

        fn on_before_popup(
            &self,
            _browser: Option<&mut Browser>,
            _frame: Option<&mut Frame>,
            _popup_id: ::std::os::raw::c_int,
            target_url: Option<&CefString>,
            _target_frame_name: Option<&CefString>,
            _target_disposition: WindowOpenDisposition,
            _user_gesture: ::std::os::raw::c_int,
            _popup_features: Option<&PopupFeatures>,
            _window_info: Option<&mut WindowInfo>,
            _client: Option<&mut Option<Client>>,
            _settings: Option<&mut BrowserSettings>,
            _extra_info: Option<&mut Option<DictionaryValue>>,
            _no_javascript_access: Option<&mut ::std::os::raw::c_int>,
        ) -> ::std::os::raw::c_int {
            // target=_blank / window.open: hand off to the user's browser instead of a CEF window.
            if let Some(url) = target_url.map(CefString::to_string)
                && (url.starts_with("http://") || url.starts_with("https://"))
            {
                info!("Opening {url} externally");
                if let Err(e) = std::process::Command::new("xdg-open").arg(&url).spawn() {
                    warning!("xdg-open failed: {e}");
                }
            }
            1
        }
    }
}

wrap_load_handler! {
    struct PopcornLoadHandler;

    impl LoadHandler {
        fn on_load_end(
            &self,
            browser: Option<&mut Browser>,
            frame: Option<&mut Frame>,
            http_status_code: ::std::os::raw::c_int,
        ) {
            if !frame.is_some_and(|f| f.is_main() == 1) {
                return;
            }
            let (on_wait_page, first_app_load) = UI.with(|ui| {
                let mut ui = ui.borrow_mut();
                let first = !ui.showing_wait_page && !ui.app_loaded_once;
                if first {
                    ui.app_loaded_once = true;
                }
                (ui.showing_wait_page, first)
            });
            // The Chrome-style window frame resets its icon to the Chromium logo when a page loads.
            if let Some(window) = window_for(browser) {
                set_window_icon(&window);
            }
            if on_wait_page {
                apply_status();
            } else if first_app_load {
                info!("UI loaded after {} ms (HTTP {http_status_code})", elapsed_ms());
            }
        }

        fn on_load_error(
            &self,
            browser: Option<&mut Browser>,
            frame: Option<&mut Frame>,
            error_code: Errorcode,
            error_text: Option<&CefString>,
            failed_url: Option<&CefString>,
        ) {
            if !frame.is_some_and(|f| f.is_main() == 1) {
                return;
            }
            if sys::cef_errorcode_t::from(error_code) == sys::cef_errorcode_t::ERR_ABORTED {
                return;
            }
            if UI.with(|ui| ui.borrow().showing_wait_page) {
                return;
            }

            let failed_url = failed_url.map(CefString::to_string).unwrap_or_default();
            let error_text = error_text.map(CefString::to_string).unwrap_or_default();
            warning!("Failed to load {failed_url}: {error_text}");

            // Server went away (restart, crash, stopped). Show the waiting page and reload once it's
            // back; don't restart the service ourselves, the user may have stopped it on purpose.
            SERVER_READY.store(false, Ordering::SeqCst);
            set_status(format!("Lost connection to {}", target().url));
            if let Some(browser) = browser {
                show_wait_page(browser);
            }
            spawn_server_watcher(false);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Waiting page + server watcher
// ---------------------------------------------------------------------------------------------

/// Polls the server in the background; navigates the browser to it once it accepts connections.
/// How the watcher expects the server to come up.
#[derive(PartialEq)]
enum Startup {
    /// Just wait (explicit URL, or a lost connection that systemd/the user should recover).
    Wait,
    /// `systemctl --user start` succeeded; watch the unit for crash loops.
    Service,
    /// We spawned Popcorn.Server ourselves.
    Direct,
    /// Nothing more we can do; keep polling in case the user starts it.
    Failed,
}

/// Polls the server in the background until it answers, then navigates to the app.
/// With `start_server`, first starts popcorn.service and, if the unit is missing or keeps failing,
/// launches Popcorn.Server from the install directory directly.
fn spawn_server_watcher(start_server: bool) {
    if WATCHER_RUNNING.swap(true, Ordering::SeqCst) {
        return;
    }

    let spawned = std::thread::Builder::new()
        .name("server-watcher".into())
        .spawn(move || {
            let target = target();
            let mut startup = if start_server {
                start_via_service()
            } else {
                Startup::Wait
            };

            let begin = Instant::now();
            let mut last_service_check = Instant::now();
            let mut warned = false;
            loop {
                if target.is_reachable() {
                    info!(
                        "Server reachable at {} after {} ms",
                        target.url,
                        elapsed_ms()
                    );
                    SERVER_READY.store(true, Ordering::SeqCst);
                    post_to_ui(NavigateTask::new());
                    break;
                }

                if startup == Startup::Service
                    && last_service_check.elapsed() >= Duration::from_secs(1)
                {
                    last_service_check = Instant::now();
                    if let Some(problem) = server::systemd_service_problem() {
                        warning!("{problem}");
                        // Stop the restart loop so it doesn't fight over the port with our own process.
                        if let Err(e) = server::stop_systemd_service() {
                            warning!("Cannot stop popcorn.service: {e}");
                        }
                        startup = launch_directly(&problem);
                    }
                }

                if !warned && begin.elapsed() > Duration::from_secs(10) {
                    warned = true;
                    warning!("Server at {} still not reachable", target.url);
                    if matches!(startup, Startup::Wait | Startup::Service) {
                        set_status(format!("No response from {}. Still trying…", target.url));
                    }
                }
                std::thread::sleep(Duration::from_millis(250));
            }

            WATCHER_RUNNING.store(false, Ordering::SeqCst);
        });

    if let Err(e) = spawned {
        WATCHER_RUNNING.store(false, Ordering::SeqCst);
        error!("Cannot spawn server watcher: {e}");
    }
}

fn start_via_service() -> Startup {
    if let Some(problem) = server::systemd_service_problem().filter(|p| p.contains("not installed"))
    {
        info!("{problem}");
        return launch_directly(&problem);
    }

    set_status("Starting popcorn.service…".to_owned());
    match server::start_systemd_service() {
        Ok(()) => {
            info!("Started popcorn.service");
            Startup::Service
        }
        Err(e) => {
            warning!("Failed to start popcorn.service: {e}");
            launch_directly(&format!("Could not start popcorn.service ({e})"))
        }
    }
}

fn launch_directly(reason: &str) -> Startup {
    set_status(format!("{reason}. Starting Popcorn.Server directly…"));
    match server::launch_server_process() {
        Ok(mut child) => {
            let pid = child.id();
            info!("Started Popcorn.Server directly (pid {pid})");
            // Reap it and report early exits; it is intentionally not killed when the window closes.
            let _ = std::thread::Builder::new().name("server-child".into()).spawn(move || {
                if let Ok(status) = child.wait() {
                    warning!("Popcorn.Server (pid {pid}) exited: {status}");
                    if !SERVER_READY.load(Ordering::SeqCst) {
                        set_status(format!(
                            "Popcorn.Server exited ({status}). See ~/.local/share/Popcorn/logs for details."
                        ));
                    }
                }
            });
            Startup::Direct
        }
        Err(e) => {
            error!("Cannot launch Popcorn.Server: {e}");
            set_status(format!(
                "{reason}. {e}. Reinstall with `make all install-linux` or start Popcorn.Server manually."
            ));
            Startup::Failed
        }
    }
}

fn post_to_ui(mut task: Task) {
    // Before the context exists `on_context_initialized` reads the shared state itself.
    if CEF_READY.load(Ordering::SeqCst) {
        post_task(ThreadId::UI, Some(&mut task));
    }
}

wrap_task! {
    struct NavigateTask;

    impl Task {
        fn execute(&self) {
            sync_navigation();
        }
    }
}

wrap_task! {
    struct StatusTask;

    impl Task {
        fn execute(&self) {
            apply_status();
        }
    }
}

/// UI thread: if the server is ready but we're still on the waiting page, load the app.
fn sync_navigation() {
    if !SERVER_READY.load(Ordering::SeqCst) {
        return;
    }
    let browser = UI.with(|ui| {
        let mut ui = ui.borrow_mut();
        if !ui.showing_wait_page {
            return None;
        }
        let browser = ui.browser.clone()?;
        ui.showing_wait_page = false;
        Some(browser)
    });
    if let Some(frame) = browser.and_then(|b| b.main_frame()) {
        frame.load_url(Some(&CefString::from(target().app_url().as_str())));
    }
}

fn show_wait_page(browser: &mut Browser) {
    UI.with(|ui| ui.borrow_mut().showing_wait_page = true);
    if let Some(frame) = browser.main_frame() {
        frame.load_url(Some(&CefString::from(wait_page_url().as_str())));
    }
}

fn set_status(status: String) {
    *STATUS.lock().unwrap() = status;
    post_to_ui(StatusTask::new());
}

/// UI thread: push the current status line into the waiting page.
fn apply_status() {
    let browser = UI.with(|ui| {
        let ui = ui.borrow();
        ui.showing_wait_page.then(|| ui.browser.clone()).flatten()
    });
    let Some(frame) = browser.and_then(|b| b.main_frame()) else {
        return;
    };
    let status = STATUS.lock().unwrap().clone();
    let js = format!(
        "document.getElementById('status').textContent = {};",
        js_string(&status)
    );
    frame.execute_java_script(Some(&CefString::from(js.as_str())), None, 0);
}

fn wait_page_url() -> String {
    let status = STATUS.lock().unwrap().clone();
    let html = format!(
        r#"<!doctype html><html><head><meta charset="utf-8"><title>{TITLE}</title><style>
html,body{{height:100%;margin:0;background:#242424;color:#ddd;font:15px system-ui,sans-serif}}
body{{display:flex;flex-direction:column;align-items:center;justify-content:center;gap:14px}}
.spinner{{width:28px;height:28px;border:3px solid #444;border-top-color:#ddd;border-radius:50%;animation:spin 1s linear infinite}}
@keyframes spin{{to{{transform:rotate(360deg)}}}}
#status{{opacity:.6;max-width:70ch;text-align:center;padding:0 24px}}
</style></head><body><div class="spinner"></div><div>Connecting to Popcorn server…</div><div id="status">{}</div></body></html>"#,
        html_escape(&status)
    );
    let encoded = CefString::from(&base64_encode(Some(html.as_bytes()))).to_string();
    format!("data:text/html;charset=utf-8;base64,{encoded}")
}

fn html_escape(s: &str) -> String {
    s.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
}

fn js_string(s: &str) -> String {
    let mut out = String::with_capacity(s.len() + 2);
    out.push('"');
    for c in s.chars() {
        match c {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '<' => out.push_str("\\u003c"),
            c if (c as u32) < 0x20 => out.push_str(&format!("\\u{:04x}", c as u32)),
            c => out.push(c),
        }
    }
    out.push('"');
    out
}
