//! The overlay: a borderless window owned by the main window, kept over its client area, with a WebView2
//! (windowed hosting, transparent background) showing the shared player UI. WebView2 handles its own input;
//! the page talks to C# over the WebSocket bridge (`window.wsAddress`), like on Linux.
//!
//! The window has no redirection bitmap (WS_EX_NOREDIRECTIONBITMAP) and paints nothing itself, so where the
//! page is transparent DWM shows what is below it: the main window with mpv's video.

use std::cell::{Cell, RefCell};

use webview2_com::Microsoft::Web::WebView2::Win32::*;
use webview2_com::*;
use windows::Win32::Foundation::{E_POINTER, HINSTANCE, HWND, LPARAM, LRESULT, RECT, WPARAM};
use windows::Win32::UI::Input::KeyboardAndMouse::{GetActiveWindow, SetActiveWindow, VK_F11};
use windows::Win32::UI::WindowsAndMessaging::*;
use windows::core::{HSTRING, Interface, PCWSTR, PWSTR, w};

use crate::log::{error, info, warning};
use crate::window;

const OVERLAY_CLASS: PCWSTR = w!("PopcornPlayerOverlay");

thread_local! {
    static OVERLAY: Cell<HWND> = const { Cell::new(HWND(std::ptr::null_mut())) };
    static CONTROLLER: RefCell<Option<ICoreWebView2Controller>> = const { RefCell::new(None) };
}

pub fn hwnd() -> HWND {
    OVERLAY.get()
}

/// The controller, cloned out: WebView2 calls can dispatch messages that re-enter the window procedure.
fn controller() -> Option<ICoreWebView2Controller> {
    CONTROLLER.with_borrow(Clone::clone)
}

pub fn create_window(owner: HWND, instance: HINSTANCE) -> windows::core::Result<()> {
    // SAFETY: Win32 calls on a window this thread creates.
    unsafe {
        let class = WNDCLASSEXW {
            cbSize: size_of::<WNDCLASSEXW>() as u32,
            lpfnWndProc: Some(overlay_proc),
            hInstance: instance,
            hCursor: LoadCursorW(None, IDC_ARROW)?,
            lpszClassName: OVERLAY_CLASS,
            ..Default::default()
        };
        if RegisterClassExW(&class) == 0 {
            return Err(windows::core::Error::from_thread());
        }
        let hwnd = CreateWindowExW(
            WS_EX_NOREDIRECTIONBITMAP,
            OVERLAY_CLASS,
            w!("Popcorn player overlay"),
            WS_POPUP | WS_CLIPCHILDREN,
            0,
            0,
            1,
            1,
            Some(owner),
            None,
            Some(instance),
            None,
        )?;
        OVERLAY.set(hwnd);
    }
    Ok(())
}

pub fn show() {
    // SAFETY: our window, on its thread.
    unsafe {
        let _ = ShowWindow(hwnd(), SW_SHOWNA);
    }
}

pub fn set_rect(x: i32, y: i32, width: i32, height: i32) {
    // SAFETY: our window, on its thread.
    unsafe {
        let _ = SetWindowPos(
            hwnd(),
            None,
            x,
            y,
            width,
            height,
            SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER,
        );
    }
}

pub fn activate() {
    // SAFETY: our window, on its thread.
    unsafe {
        let _ = SetActiveWindow(hwnd());
    }
}

/// Closes the WebView2 (window closing; the browser processes exit with the last one).
pub fn close() {
    if let Some(controller) = CONTROLLER.with_borrow_mut(Option::take) {
        // SAFETY: COM call on the UI thread.
        let _ = unsafe { controller.Close() };
    }
    // SAFETY: our window, on its thread.
    unsafe {
        let _ = ShowWindow(hwnd(), SW_HIDE);
    }
}

pub fn destroy() {
    close();
    let hwnd = OVERLAY.replace(HWND(std::ptr::null_mut()));
    if !hwnd.0.is_null() {
        // SAFETY: our window, on its thread.
        unsafe {
            let _ = DestroyWindow(hwnd);
        }
    }
}

/// Starts creating the WebView2. Asynchronous: the steps continue from the message loop; failures are logged.
pub fn start_webview() {
    if let Err(e) = create_environment() {
        error!("Could not start WebView2: {e}");
    }
}

fn create_environment() -> windows::core::Result<()> {
    let config = crate::config();
    // Transparent from the first frame on, before SetDefaultBackgroundColor applies (else a white flash).
    // SAFETY: set before any other thread of ours reads the environment (only WebView2's loader does).
    unsafe { std::env::set_var("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00000000") };

    // LocalNetworkAccess*: the overlay (http://app.local, a "public" origin to Chromium) must reach the C#
    // WebSocket on 127.0.0.1 (same as the Linux player's CEF).
    let mut arguments =
        String::from("--disable-features=LocalNetworkAccessChecks,LocalNetworkAccessChecksWebSockets");
    if config.devtools_port > 0 {
        arguments.push_str(&format!(" --remote-debugging-port={}", config.devtools_port));
    }
    let options = CoreWebView2EnvironmentOptions::default();
    // SAFETY: not shared yet.
    unsafe { options.set_additional_browser_arguments(arguments) };
    let options: ICoreWebView2EnvironmentOptions = options.into();

    let handler = CreateCoreWebView2EnvironmentCompletedHandler::create(Box::new(|result, environment| {
        if let Err(e) = result.and_then(|_| environment.ok_or_else(|| E_POINTER.into())).and_then(create_controller) {
            error!("Could not create the WebView2 environment: {e}");
        }
        Ok(())
    }));
    let user_data = HSTRING::from(config.user_data_dir.as_os_str());
    // SAFETY: COM call on the UI thread; the handler runs from its message loop.
    unsafe { CreateCoreWebView2EnvironmentWithOptions(PCWSTR::null(), &user_data, &options, &handler) }
}

fn create_controller(environment: ICoreWebView2Environment) -> windows::core::Result<()> {
    let handler = CreateCoreWebView2ControllerCompletedHandler::create(Box::new(|result, controller| {
        if let Err(e) = result.and_then(|_| controller.ok_or_else(|| E_POINTER.into())).and_then(set_up) {
            error!("Could not create the WebView2: {e}");
        }
        Ok(())
    }));
    // SAFETY: COM call on the UI thread.
    unsafe { environment.CreateCoreWebView2Controller(hwnd(), &handler) }
}

fn set_up(controller: ICoreWebView2Controller) -> windows::core::Result<()> {
    let config = crate::config();
    // SAFETY: COM calls on the UI thread.
    unsafe {
        controller
            .cast::<ICoreWebView2Controller2>()?
            .SetDefaultBackgroundColor(COREWEBVIEW2_COLOR { A: 0, R: 0, G: 0, B: 0 })?;
        let mut bounds = RECT::default();
        GetClientRect(hwnd(), &mut bounds)?;
        controller.SetBounds(bounds)?;

        let webview = controller.CoreWebView2()?;
        let settings = webview.Settings()?;
        settings.SetAreDefaultContextMenusEnabled(false)?;
        settings.SetIsZoomControlEnabled(false)?;
        settings.SetIsStatusBarEnabled(false)?;
        // SmartScreen (on by default) checks every http(s) navigation; for app.local it holds each page ~2 s
        // before parsing it, so the UI showed up long after the video.
        settings.cast::<ICoreWebView2Settings8>()?.SetIsReputationCheckingRequired(false)?;

        webview.AddScriptToExecuteOnDocumentCreated(&HSTRING::from(config.inject_script.as_str()), None)?;
        webview.cast::<ICoreWebView2_3>()?.SetVirtualHostNameToFolderMapping(
            w!("app.local"),
            &HSTRING::from(config.overlay_dir.as_os_str()),
            COREWEBVIEW2_HOST_RESOURCE_ACCESS_KIND_ALLOW,
        )?;

        let mut token = 0i64;
        webview.add_WebMessageReceived(&WebMessageReceivedEventHandler::create(Box::new(on_web_message)), &mut token)?;
        // Links never open windows of their own.
        webview.add_NewWindowRequested(
            &NewWindowRequestedEventHandler::create(Box::new(|_, args| {
                if let Some(args) = args {
                    args.SetHandled(true)?;
                }
                Ok(())
            })),
            &mut token,
        )?;
        webview.add_ProcessFailed(
            &ProcessFailedEventHandler::create(Box::new(|_, args| {
                if let Some(args) = args {
                    let mut kind = COREWEBVIEW2_PROCESS_FAILED_KIND::default();
                    args.ProcessFailedKind(&mut kind)?;
                    warning!("WebView2 process failed (kind {})", kind.0);
                }
                Ok(())
            })),
            &mut token,
        )?;
        controller.add_AcceleratorKeyPressed(
            &AcceleratorKeyPressedEventHandler::create(Box::new(on_accelerator_key)),
            &mut token,
        )?;

        controller.SetIsVisible(true)?;
        CONTROLLER.with_borrow_mut(|c| *c = Some(controller.clone()));
        let url = format!("http://app.local/{}", config.overlay_page);
        info!("Overlay: {url} from {}", config.overlay_dir.display());
        webview.Navigate(&HSTRING::from(url))?;
        if GetActiveWindow() == hwnd() {
            controller.MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC)?;
        }
    }
    crate::emit_window_event(crate::EVENT_READY, 0);
    Ok(())
}

/// F11 = fullscreen, like the Linux player (F12 = DevTools is WebView2's own).
fn on_accelerator_key(
    _: Option<ICoreWebView2Controller>,
    args: Option<ICoreWebView2AcceleratorKeyPressedEventArgs>,
) -> windows::core::Result<()> {
    let Some(args) = args else { return Ok(()) };
    // SAFETY: COM calls on the UI thread.
    unsafe {
        let mut kind = COREWEBVIEW2_KEY_EVENT_KIND::default();
        args.KeyEventKind(&mut kind)?;
        let mut key = 0u32;
        args.VirtualKey(&mut key)?;
        if kind == COREWEBVIEW2_KEY_EVENT_KIND_KEY_DOWN && key == VK_F11.0 as u32 {
            args.SetHandled(true)?;
            window::set_fullscreen(!window::is_fullscreen());
        }
    }
    Ok(())
}

/// The page posts `files_dropped` with the dropped `File`s as additional objects (NativePlayer.tsx), which
/// is how a page hands WebView2 files whose paths the host can read.
fn on_web_message(
    _: Option<ICoreWebView2>,
    args: Option<ICoreWebView2WebMessageReceivedEventArgs>,
) -> windows::core::Result<()> {
    let Some(args) = args else { return Ok(()) };
    // SAFETY: COM calls on the UI thread.
    unsafe {
        let mut message = PWSTR::null();
        if args.TryGetWebMessageAsString(&mut message).is_err() {
            return Ok(());
        }
        if take_pwstr(message) != "files_dropped" {
            return Ok(());
        }
        let objects = args.cast::<ICoreWebView2WebMessageReceivedEventArgs2>()?.AdditionalObjects()?;
        let mut count = 0u32;
        objects.Count(&mut count)?;
        let mut paths = Vec::new();
        for i in 0..count {
            let object = objects.GetValueAtIndex(i)?;
            if let Ok(file) = object.cast::<ICoreWebView2File>() {
                let mut path = PWSTR::null();
                file.Path(&mut path)?;
                paths.push(take_pwstr(path));
            }
        }
        info!("Files dropped: {}", paths.len());
        if !paths.is_empty() {
            crate::emit_files_dropped(&paths);
        }
    }
    Ok(())
}

extern "system" fn overlay_proc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    // SAFETY: called by Windows for our window, on its thread.
    unsafe {
        match msg {
            WM_SIZE => {
                if let Some(controller) = controller() {
                    let mut bounds = RECT::default();
                    let _ = GetClientRect(hwnd, &mut bounds);
                    let _ = controller.SetBounds(bounds);
                }
            }
            WM_MOVE => {
                if let Some(controller) = controller() {
                    let _ = controller.NotifyParentWindowPositionChanged();
                }
            }
            WM_SETFOCUS => {
                if let Some(controller) = controller() {
                    let _ = controller.MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC);
                }
            }
            WM_ACTIVATE => {
                let state = (wparam.0 & 0xFFFF) as u32;
                // The main window's caption shows the state of the pair.
                if state != WA_INACTIVE {
                    window::paint_active(true);
                } else if lparam.0 != window::main_hwnd() {
                    window::paint_active(false);
                }
            }
            WM_MOUSEACTIVATE => return LRESULT(MA_ACTIVATE as isize),
            WM_ERASEBKGND => return LRESULT(1),
            // Alt+F4 while the overlay is active closes the player, not just the overlay.
            WM_CLOSE => {
                window::post_close();
                return LRESULT(0);
            }
            _ => {}
        }
        DefWindowProcW(hwnd, msg, wparam, lparam)
    }
}
