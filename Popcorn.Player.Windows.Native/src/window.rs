//! The main window (frame, title bar, mpv's video inside) and its message loop. The overlay window
//! (overlay.rs) follows it: same screen rect as main's client area, activated in its place.

use std::cell::Cell;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicIsize, AtomicU32, Ordering};

use windows::Win32::Foundation::{HWND, LPARAM, LRESULT, POINT, RECT, WPARAM};
use windows::Win32::Graphics::Gdi::{
    BLACK_BRUSH, BeginPaint, ClientToScreen, EndPaint, PAINTSTRUCT, GetMonitorInfoW, GetStockObject, HBRUSH, MONITOR_DEFAULTTONEAREST, MONITORINFO,
    MonitorFromWindow,
};
use windows::Win32::System::Com::{COINIT_APARTMENTTHREADED, CoInitializeEx};
use windows::Win32::System::LibraryLoader::{GetModuleFileNameW, GetModuleHandleW};
use windows::Win32::System::Threading::{AttachThreadInput, GetCurrentThreadId};
use windows::Win32::UI::HiDpi::{
    DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2, GetDpiForSystem, GetDpiForWindow, SetProcessDpiAwarenessContext,
};
use windows::Win32::UI::Input::KeyboardAndMouse::{GetActiveWindow, GetAsyncKeyState, VK_LBUTTON, VK_RBUTTON};
use windows::Win32::UI::Shell::ExtractIconExW;
use windows::Win32::UI::WindowsAndMessaging::*;
use windows::core::{PCWSTR, w};

use crate::log::{info, warning};
use crate::{overlay, splash};

const MAIN_CLASS: PCWSTR = w!("PopcornPlayer");
const WM_APP_FULLSCREEN: u32 = WM_APP + 1;
const WM_APP_TITLE: u32 = WM_APP + 2;
const WM_APP_ACTIVATE_OVERLAY: u32 = WM_APP + 3;
const WM_APP_HIDE_SPLASH: u32 = WM_APP + 4;
const TIMER_ACTIVATE_OVERLAY: usize = 1;
/// Default client size in DIPs (the old WPF player's window).
const DEFAULT_SIZE: (i32, i32) = (1000, 600);
const MIN_SIZE: (i32, i32) = (400, 250);

/// Main window handle, for the cross-thread functions below.
static MAIN: AtomicIsize = AtomicIsize::new(0);
static UI_THREAD: AtomicU32 = AtomicU32::new(0);

thread_local! {
    static FULLSCREEN: Cell<bool> = const { Cell::new(false) };
    /// Placement before fullscreen, restored after it.
    static RESTORE: Cell<WINDOWPLACEMENT> = Cell::new(WINDOWPLACEMENT::default());
}

pub fn main_hwnd() -> isize {
    MAIN.load(Ordering::SeqCst)
}

fn main_window() -> HWND {
    HWND(main_hwnd() as *mut _)
}

/// Creates both windows, hidden. Also sets up the thread for them (COM apartment) and the process DPI mode.
pub fn create() -> windows::core::Result<()> {
    // SAFETY: Win32 calls on windows this thread creates and owns.
    unsafe {
        // Per-monitor DPI: otherwise Windows bitmap-scales the whole window (blurry video and UI).
        if let Err(e) = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) {
            warning!("Could not switch to per-monitor DPI awareness: {e}");
        }
        // WebView2 needs a single-threaded apartment (C#: [STAThread] on Main).
        let hr = CoInitializeEx(None, COINIT_APARTMENTTHREADED);
        if hr.is_err() {
            warning!("CoInitializeEx: {hr:?}");
        }
        UI_THREAD.store(GetCurrentThreadId(), Ordering::SeqCst);

        let instance = GetModuleHandleW(None)?.into();
        let (icon_large, icon_small) = exe_icons();
        let class = WNDCLASSEXW {
            cbSize: size_of::<WNDCLASSEXW>() as u32,
            lpfnWndProc: Some(main_proc),
            hInstance: instance,
            hIcon: icon_large,
            hIconSm: icon_small,
            // Repaint all on resize: the logo stays centred.
            style: CS_HREDRAW | CS_VREDRAW,
            hCursor: LoadCursorW(None, IDC_ARROW)?,
            // Black (+ the logo while the overlay starts) until mpv's video window covers it.
            hbrBackground: HBRUSH(GetStockObject(BLACK_BRUSH).0),
            lpszClassName: MAIN_CLASS,
            ..Default::default()
        };
        if RegisterClassExW(&class) == 0 {
            return Err(windows::core::Error::from_thread());
        }

        let config = crate::config();
        let title: Vec<u16> = config.title.encode_utf16().chain([0]).collect();
        let (x, y, width, height) = default_rect();
        let hwnd = CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            MAIN_CLASS,
            PCWSTR(title.as_ptr()),
            WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN,
            x,
            y,
            width,
            height,
            None,
            None,
            Some(instance),
            None,
        )?;
        MAIN.store(hwnd.0 as isize, Ordering::SeqCst);
        splash::load();
        overlay::create_window(hwnd, instance)?;
    }
    Ok(())
}

/// Default size, centred in the primary monitor's work area.
fn default_rect() -> (i32, i32, i32, i32) {
    // SAFETY: plain Win32 queries.
    unsafe {
        let dpi = GetDpiForSystem() as i32;
        let mut frame = RECT { left: 0, top: 0, right: DEFAULT_SIZE.0 * dpi / 96, bottom: DEFAULT_SIZE.1 * dpi / 96 };
        let _ = windows::Win32::UI::HiDpi::AdjustWindowRectExForDpi(
            &mut frame,
            WS_OVERLAPPEDWINDOW,
            false,
            WINDOW_EX_STYLE::default(),
            dpi as u32,
        );
        let (width, height) = (frame.right - frame.left, frame.bottom - frame.top);
        let mut work = RECT::default();
        if SystemParametersInfoW(SPI_GETWORKAREA, 0, Some(&mut work as *mut _ as *mut _), Default::default()).is_err() {
            return (CW_USEDEFAULT, CW_USEDEFAULT, width, height);
        }
        let width = width.min(work.right - work.left);
        let height = height.min(work.bottom - work.top);
        (work.left + (work.right - work.left - width) / 2, work.top + (work.bottom - work.top - height) / 2, width, height)
    }
}

/// The executable's own icon (.NET puts the project's ApplicationIcon there).
fn exe_icons() -> (HICON, HICON) {
    // SAFETY: plain Win32 calls with buffers we own.
    unsafe {
        let mut path = [0u16; 1024];
        let len = GetModuleFileNameW(None, &mut path) as usize;
        if len == 0 || len >= path.len() {
            return (HICON::default(), HICON::default());
        }
        let (mut large, mut small) = (HICON::default(), HICON::default());
        ExtractIconExW(PCWSTR(path.as_ptr()), 0, Some(&mut large), Some(&mut small), 1);
        (large, small)
    }
}

pub fn run() -> windows::core::Result<()> {
    let main = main_window();
    // SAFETY: windows of this thread.
    unsafe {
        let _ = ShowWindow(main, SW_SHOW);
        bring_to_foreground(main);
        sync_overlay(main);
        overlay::show();
        overlay::start_webview();
        activate_overlay(main);
    }
    info!("Player window shown");

    let mut msg = MSG::default();
    // SAFETY: standard message loop.
    unsafe {
        loop {
            match GetMessageW(&mut msg, None, 0, 0).0 {
                -1 => return Err(windows::core::Error::from_thread()),
                0 => return Ok(()),
                _ => {
                    let _ = TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
            }
        }
    }
}

/// Runs `work` on another thread and dispatches this thread's messages until it is done.
pub fn pump_until(work: impl FnOnce() + Send + 'static) {
    let done = Arc::new(AtomicBool::new(false));
    let thread_id = UI_THREAD.load(Ordering::SeqCst);
    let worker = {
        let done = done.clone();
        std::thread::spawn(move || {
            work();
            done.store(true, Ordering::SeqCst);
            // Wake GetMessageW up.
            // SAFETY: posting to a thread's queue.
            unsafe {
                let _ = PostThreadMessageW(thread_id, WM_NULL, WPARAM(0), LPARAM(0));
            }
        })
    };
    let mut msg = MSG::default();
    // SAFETY: message loop on the UI thread.
    unsafe {
        while !done.load(Ordering::SeqCst) {
            if GetMessageW(&mut msg, None, 0, 0).0 <= 0 {
                break;
            }
            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
    let _ = worker.join();
}

pub fn destroy() {
    let main = MAIN.swap(0, Ordering::SeqCst);
    if main != 0 {
        overlay::destroy();
        // SAFETY: our window, on its thread.
        unsafe {
            let _ = DestroyWindow(HWND(main as *mut _));
        }
    }
    splash::free();
}

fn post(msg: u32, wparam: usize, lparam: isize) -> bool {
    let main = main_hwnd();
    // SAFETY: posting to our window; fails harmlessly once it is gone.
    main != 0 && unsafe { PostMessageW(Some(HWND(main as *mut _)), msg, WPARAM(wparam), LPARAM(lparam)) }.is_ok()
}

pub fn post_close() {
    post(WM_CLOSE, 0, 0);
}

pub fn post_fullscreen(on: bool) {
    post(WM_APP_FULLSCREEN, on as usize, 0);
}

pub fn post_hide_splash() {
    post(WM_APP_HIDE_SPLASH, 0, 0);
}

pub fn post_title(title: String) {
    let title: Box<Vec<u16>> = Box::new(title.encode_utf16().chain([0]).collect());
    let ptr = Box::into_raw(title);
    if !post(WM_APP_TITLE, 0, ptr as isize) {
        // SAFETY: not posted, still ours.
        drop(unsafe { Box::from_raw(ptr) });
    }
}

pub fn is_fullscreen() -> bool {
    FULLSCREEN.get()
}

/// Borderless over the whole monitor, or back to the placement from before.
pub fn set_fullscreen(on: bool) {
    if on == FULLSCREEN.get() {
        return;
    }
    let main = main_window();
    // SAFETY: our window, on its thread.
    unsafe {
        let style = WINDOW_STYLE(GetWindowLongPtrW(main, GWL_STYLE) as u32);
        if on {
            let mut placement = WINDOWPLACEMENT { length: size_of::<WINDOWPLACEMENT>() as u32, ..Default::default() };
            let _ = GetWindowPlacement(main, &mut placement);
            RESTORE.set(placement);
            let mut monitor = MONITORINFO { cbSize: size_of::<MONITORINFO>() as u32, ..Default::default() };
            let _ = GetMonitorInfoW(MonitorFromWindow(main, MONITOR_DEFAULTTONEAREST), &mut monitor);
            let r = monitor.rcMonitor;
            SetWindowLongPtrW(main, GWL_STYLE, ((style & !WS_OVERLAPPEDWINDOW) | WS_POPUP).0 as isize);
            let _ = SetWindowPos(
                main,
                Some(HWND_TOP),
                r.left,
                r.top,
                r.right - r.left,
                r.bottom - r.top,
                SWP_FRAMECHANGED | SWP_NOOWNERZORDER,
            );
        } else {
            SetWindowLongPtrW(main, GWL_STYLE, ((style & !WS_POPUP) | WS_OVERLAPPEDWINDOW).0 as isize);
            let placement = RESTORE.get();
            if placement.length != 0 {
                let _ = SetWindowPlacement(main, &placement);
            }
            let _ = SetWindowPos(
                main,
                None,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED,
            );
        }
    }
    FULLSCREEN.set(on);
    sync_overlay(main);
    crate::emit_window_event(crate::EVENT_FULLSCREEN_CHANGED, on as i32);
}

/// Puts the overlay window over main's client area.
fn sync_overlay(main: HWND) {
    // SAFETY: our windows, on their thread.
    unsafe {
        if IsIconic(main).as_bool() {
            return;
        }
        let mut client = RECT::default();
        if GetClientRect(main, &mut client).is_err() {
            return;
        }
        let mut origin = POINT { x: 0, y: 0 };
        let _ = ClientToScreen(main, &mut origin);
        overlay::set_rect(origin.x, origin.y, client.right, client.bottom);
    }
}

fn left_button_down() -> bool {
    // GetAsyncKeyState reads the physical buttons.
    // SAFETY: plain Win32 queries.
    unsafe {
        let button = if GetSystemMetrics(SM_SWAPBUTTON) != 0 { VK_RBUTTON } else { VK_LBUTTON };
        GetAsyncKeyState(button.0 as i32) < 0
    }
}

/// Main got activated (taskbar, Alt+Tab, title bar): hand it on to the overlay, which has the keyboard
/// focus. Not while the title bar is held (dragging): activating another window would end the drag.
/// The server (a background process) starts the player, so Windows' foreground lock would leave it
/// behind the app the user clicked in. Attaching to the foreground window's input queue lets
/// SetForegroundWindow through.
fn bring_to_foreground(main: HWND) {
    // SAFETY: plain Win32 calls; the input queues are detached again before returning.
    unsafe {
        let foreground = GetForegroundWindow();
        if foreground == main {
            return;
        }
        let this_thread = GetCurrentThreadId();
        let foreground_thread = if foreground.is_invalid() { 0 } else { GetWindowThreadProcessId(foreground, None) };
        let attached = foreground_thread != 0
            && foreground_thread != this_thread
            && AttachThreadInput(this_thread, foreground_thread, true).as_bool();
        let _ = BringWindowToTop(main);
        if !SetForegroundWindow(main).as_bool() {
            warning!("Could not bring the player window to the foreground");
        }
        if attached {
            let _ = AttachThreadInput(this_thread, foreground_thread, false);
        }
    }
}

fn activate_overlay(main: HWND) {
    // SAFETY: our windows, on their thread.
    unsafe {
        if left_button_down() {
            SetTimer(Some(main), TIMER_ACTIVATE_OVERLAY, 50, None);
            return;
        }
        let _ = KillTimer(Some(main), TIMER_ACTIVATE_OVERLAY);
        if GetActiveWindow() == main && !IsIconic(main).as_bool() {
            overlay::activate();
        }
    }
}

/// The overlay is active instead of main, which should still look like the active window.
pub fn paint_active(active: bool) {
    let main = main_window();
    // SAFETY: our window, on its thread. DefWindowProc draws (and tells DWM) the caption state.
    unsafe {
        DefWindowProcW(main, WM_NCACTIVATE, WPARAM(active as usize), LPARAM(0));
    }
}

extern "system" fn main_proc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    // SAFETY: called by Windows for our window, on its thread.
    unsafe {
        match msg {
            WM_SIZE | WM_MOVE => {
                sync_overlay(hwnd);
            }
            WM_ACTIVATE => {
                let state = (wparam.0 & 0xFFFF) as u32;
                if state == WA_INACTIVE {
                    // Losing activation to our own overlay: keep the active caption.
                    if HWND(lparam.0 as *mut _) == overlay::hwnd() {
                        paint_active(true);
                    }
                } else {
                    let _ = PostMessageW(Some(hwnd), WM_APP_ACTIVATE_OVERLAY, WPARAM(0), LPARAM(0));
                }
            }
            WM_APP_ACTIVATE_OVERLAY => {
                activate_overlay(hwnd);
                return LRESULT(0);
            }
            WM_TIMER if wparam.0 == TIMER_ACTIVATE_OVERLAY => {
                activate_overlay(hwnd);
                return LRESULT(0);
            }
            WM_GETMINMAXINFO => {
                let info = &mut *(lparam.0 as *mut MINMAXINFO);
                let dpi = GetDpiForWindow(hwnd).max(96) as i32;
                info.ptMinTrackSize = POINT { x: MIN_SIZE.0 * dpi / 96, y: MIN_SIZE.1 * dpi / 96 };
                return LRESULT(0);
            }
            WM_DPICHANGED => {
                let r = &*(lparam.0 as *const RECT);
                let _ = SetWindowPos(
                    hwnd,
                    None,
                    r.left,
                    r.top,
                    r.right - r.left,
                    r.bottom - r.top,
                    SWP_NOZORDER | SWP_NOACTIVATE,
                );
                return LRESULT(0);
            }
            WM_APP_FULLSCREEN => {
                set_fullscreen(wparam.0 != 0);
                return LRESULT(0);
            }
            WM_PAINT => {
                let mut paint = PAINTSTRUCT::default();
                let hdc = BeginPaint(hwnd, &mut paint);
                splash::paint(hwnd, hdc);
                let _ = EndPaint(hwnd, &paint);
                return LRESULT(0);
            }
            WM_APP_HIDE_SPLASH => {
                splash::hide(hwnd);
                return LRESULT(0);
            }
            WM_APP_TITLE => {
                let title = Box::from_raw(lparam.0 as *mut Vec<u16>);
                let _ = SetWindowTextW(hwnd, PCWSTR(title.as_ptr()));
                return LRESULT(0);
            }
            WM_CLOSE => {
                info!("Player window closing");
                crate::emit_window_event(crate::EVENT_CLOSE_REQUESTED, 0);
                // Hidden but not destroyed: mpv's video window inside it lives until pn_destroy stops mpv.
                overlay::close();
                let _ = ShowWindow(hwnd, SW_HIDE);
                PostQuitMessage(0);
                return LRESULT(0);
            }
            _ => {}
        }
        DefWindowProcW(hwnd, msg, wparam, lparam)
    }
}
