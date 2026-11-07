//! The Popcorn logo in the middle of the main window until there is something else to show. The overlay window
//! above is transparent where the page draws nothing, so the logo shows through it. C# hides it (`pn_hide_splash`)
//! with the first video frame, or when the load ends without one (no file, error). Drawn with GDI+ (PNG with
//! alpha, smooth scaling), the image is built into the DLL.

use std::cell::Cell;

use windows::Win32::Foundation::{HWND, RECT};
use windows::Win32::Graphics::Gdi::{HDC, InvalidateRect};
use windows::Win32::Graphics::GdiPlus::{
    GdipCreateBitmapFromStream, GdipCreateFromHDC, GdipDeleteGraphics, GdipDisposeImage, GdipDrawImageRectI,
    GdipSetInterpolationMode, GdipSetPixelOffsetMode, GdiplusShutdown, GdiplusStartup, GdiplusStartupInput, GpBitmap,
    GpImage, InterpolationModeHighQualityBicubic, PixelOffsetModeHighQuality, Status,
};
use windows::Win32::UI::HiDpi::GetDpiForWindow;
use windows::Win32::UI::Shell::SHCreateMemStream;
use windows::Win32::UI::WindowsAndMessaging::GetClientRect;

use crate::log::{info, warning};

const OK: Status = Status(0);
const PNG: &[u8] = include_bytes!("../../Popcorn.Player.Windows/popcorn.png");
/// Logo size in DIPs; smaller in small windows.
const SIZE: i32 = 160;

thread_local! {
    static TOKEN: Cell<usize> = const { Cell::new(0) };
    static IMAGE: Cell<*mut GpBitmap> = const { Cell::new(std::ptr::null_mut()) };
}

/// Decodes the logo. Without it (logged) the window is just black.
pub fn load() {
    // SAFETY: GDI+ calls on the UI thread with pointers we own.
    unsafe {
        let mut token = 0usize;
        let input = GdiplusStartupInput { GdiplusVersion: 1, ..Default::default() };
        let status = GdiplusStartup(&mut token, &input, std::ptr::null_mut());
        if status != OK {
            warning!("GdiplusStartup: {}", status.0);
            return;
        }
        TOKEN.set(token);
        let Some(stream) = SHCreateMemStream(Some(PNG)) else { return };
        let mut bitmap = std::ptr::null_mut();
        let status = GdipCreateBitmapFromStream(&stream, &mut bitmap);
        if status != OK {
            warning!("Splash image: GDI+ status {}", status.0);
            return;
        }
        IMAGE.set(bitmap);
    }
}

/// WM_PAINT of the main window, after its background has been erased.
pub fn paint(hwnd: HWND, hdc: HDC) {
    let image = IMAGE.get();
    if image.is_null() {
        return;
    }
    // SAFETY: GDI+ calls on the UI thread; `hdc` is the window's paint DC.
    unsafe {
        let mut client = RECT::default();
        if GetClientRect(hwnd, &mut client).is_err() {
            return;
        }
        let (width, height) = (client.right, client.bottom);
        let dpi = GetDpiForWindow(hwnd).max(96) as i32;
        let size = (SIZE * dpi / 96).min(width.min(height) / 2);
        if size <= 0 {
            return;
        }
        let mut graphics = std::ptr::null_mut();
        if GdipCreateFromHDC(hdc, &mut graphics) != OK {
            return;
        }
        GdipSetInterpolationMode(graphics, InterpolationModeHighQualityBicubic);
        GdipSetPixelOffsetMode(graphics, PixelOffsetModeHighQuality);
        GdipDrawImageRectI(graphics, image as *mut GpImage, (width - size) / 2, (height - size) / 2, size, size);
        GdipDeleteGraphics(graphics);
    }
}

/// Gone for good (the page is up); the window repaints black.
pub fn hide(hwnd: HWND) {
    if IMAGE.get().is_null() {
        return;
    }
    info!("Splash hidden");
    free();
    // SAFETY: our window.
    unsafe {
        let _ = InvalidateRect(Some(hwnd), None, true);
    }
}

pub fn free() {
    let image = IMAGE.replace(std::ptr::null_mut());
    // SAFETY: our GDI+ objects, on the thread that made them.
    unsafe {
        if !image.is_null() {
            GdipDisposeImage(image as *mut GpImage);
        }
        let token = TOKEN.replace(0);
        if token != 0 {
            GdiplusShutdown(token);
        }
    }
}
