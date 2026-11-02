//! Log messages are forwarded to C# (Serilog) through `PnCallbacks::log`.

pub const DEBUG: i32 = 0;
pub const INFO: i32 = 1;
pub const WARNING: i32 = 2;
pub const ERROR: i32 = 3;

macro_rules! debug {
    ($($arg:tt)*) => { $crate::emit_log($crate::log::DEBUG, &format!($($arg)*)) };
}
macro_rules! info {
    ($($arg:tt)*) => { $crate::emit_log($crate::log::INFO, &format!($($arg)*)) };
}
macro_rules! warning {
    ($($arg:tt)*) => { $crate::emit_log($crate::log::WARNING, &format!($($arg)*)) };
}
macro_rules! error {
    ($($arg:tt)*) => { $crate::emit_log($crate::log::ERROR, &format!($($arg)*)) };
}

#[allow(unused_imports)]
pub(crate) use {debug, error, info, warning};
