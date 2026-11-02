//! xkb keysym / evdev scancode -> Windows virtual-key code, which CEF uses on every platform
//! (`KeyEvent::windows_key_code`). Roughly Chromium's `KeyboardCodeFromXKeysym`, trimmed to keys
//! a video player overlay can care about.

/// `cef_event_flags_t` bits.
pub mod flags {
    pub const CAPS_LOCK: u32 = 1 << 0;
    pub const SHIFT: u32 = 1 << 1;
    pub const CONTROL: u32 = 1 << 2;
    pub const ALT: u32 = 1 << 3;
    pub const LEFT_MOUSE: u32 = 1 << 4;
    pub const MIDDLE_MOUSE: u32 = 1 << 5;
    pub const RIGHT_MOUSE: u32 = 1 << 6;
    pub const NUM_LOCK: u32 = 1 << 8;
    pub const IS_KEY_PAD: u32 = 1 << 9;
}

pub const VK_F11: i32 = 0x7A;
pub const VK_F12: i32 = 0x7B;

/// Returns (windows key code, is keypad key).
pub fn windows_key_code(keysym: u32, evdev: u32) -> (i32, bool) {
    let vk = match keysym {
        0x61..=0x7a => keysym as i32 - 0x61 + 0x41, // a-z
        0x41..=0x5a => keysym as i32,               // A-Z
        0x30..=0x39 => keysym as i32,               // 0-9
        0x20 => 0x20,
        // Shifted digits on a US layout.
        0x29 => 0x30,
        0x21 => 0x31,
        0x40 => 0x32,
        0x23 => 0x33,
        0x24 => 0x34,
        0x25 => 0x35,
        0x5e => 0x36,
        0x26 => 0x37,
        0x2a => 0x38,
        0x28 => 0x39,
        // OEM punctuation.
        0x3b | 0x3a => 0xBA,
        0x3d | 0x2b => 0xBB,
        0x2c | 0x3c => 0xBC,
        0x2d | 0x5f => 0xBD,
        0x2e | 0x3e => 0xBE,
        0x2f | 0x3f => 0xBF,
        0x60 | 0x7e => 0xC0,
        0x5b | 0x7b => 0xDB,
        0x5c | 0x7c => 0xDC,
        0x5d | 0x7d => 0xDD,
        0x27 | 0x22 => 0xDE,
        0xff08 => 0x08,          // BackSpace
        0xff09 | 0xfe20 => 0x09, // Tab, ISO_Left_Tab
        0xff0d => 0x0D,          // Return
        0xff13 => 0x13,          // Pause
        0xff1b => 0x1B,          // Escape
        0xff50 => 0x24,          // Home
        0xff51 => 0x25,          // Left
        0xff52 => 0x26,          // Up
        0xff53 => 0x27,          // Right
        0xff54 => 0x28,          // Down
        0xff55 => 0x21,          // Prior
        0xff56 => 0x22,          // Next
        0xff57 => 0x23,          // End
        0xff61 => 0x2C,          // Print
        0xff63 => 0x2D,          // Insert
        0xffff => 0x2E,          // Delete
        0xff67 => 0x5D,          // Menu
        0xff7f => 0x90,          // Num_Lock
        0xff14 => 0x91,          // Scroll_Lock
        0xffe1 | 0xffe2 => 0x10, // Shift
        0xffe3 | 0xffe4 => 0x11, // Control
        0xffe9 | 0xffea | 0xfe03 => 0x12, // Alt, ISO_Level3_Shift (AltGr)
        0xffe5 => 0x14,          // Caps_Lock
        0xffeb => 0x5B,          // Super_L
        0xffec => 0x5C,          // Super_R
        0xffbe..=0xffd5 => keysym as i32 - 0xffbe + 0x70, // F1-F24
        // XF86 media keys.
        0x1008ff11 => 0xAE,
        0x1008ff12 => 0xAD,
        0x1008ff13 => 0xAF,
        0x1008ff14 => 0xB3,
        0x1008ff15 => 0xB2,
        0x1008ff16 => 0xB1,
        0x1008ff17 => 0xB0,
        _ => 0,
    };
    if vk != 0 {
        return (vk, false);
    }

    let keypad = match keysym {
        0xffb0..=0xffb9 => keysym as i32 - 0xffb0 + 0x60, // KP_0-9
        0xffaa => 0x6A,
        0xffab => 0x6B,
        0xffad => 0x6D,
        0xffae => 0x6E,
        0xffaf => 0x6F,
        0xff8d => 0x0D, // KP_Enter
        0xff95 => 0x24,
        0xff96 => 0x25,
        0xff97 => 0x26,
        0xff98 => 0x27,
        0xff99 => 0x28,
        0xff9a => 0x21,
        0xff9b => 0x22,
        0xff9c => 0x23,
        0xff9e => 0x2D,
        0xff9f => 0x2E,
        _ => 0,
    };
    if keypad != 0 {
        return (keypad, true);
    }

    // Non-Latin layouts: fall back to the key's US position.
    (evdev_us_key_code(evdev), false)
}

fn evdev_us_key_code(code: u32) -> i32 {
    const ROW_Q: &[u8] = b"QWERTYUIOP";
    const ROW_A: &[u8] = b"ASDFGHJKL";
    const ROW_Z: &[u8] = b"ZXCVBNM";
    match code {
        2..=10 => 0x31 + (code as i32 - 2), // 1-9
        11 => 0x30,
        16..=25 => ROW_Q[(code - 16) as usize] as i32,
        30..=38 => ROW_A[(code - 30) as usize] as i32,
        44..=50 => ROW_Z[(code - 44) as usize] as i32,
        _ => 0,
    }
}
