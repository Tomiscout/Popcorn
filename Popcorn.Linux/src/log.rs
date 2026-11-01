//! Minimal logger: stderr + daily file in `<data>/logs/Popcorn.Linux-YYYYMMDD.txt`.

use std::fs::{File, OpenOptions};
use std::io::Write;
use std::path::Path;
use std::sync::Mutex;

static FILE: Mutex<Option<File>> = Mutex::new(None);

pub fn init(logs_dir: &Path) {
    let _ = std::fs::create_dir_all(logs_dir);
    let t = LocalTime::now();
    let path = logs_dir.join(format!(
        "Popcorn.Linux-{:04}{:02}{:02}.txt",
        t.year, t.month, t.day
    ));
    match OpenOptions::new().create(true).append(true).open(&path) {
        Ok(file) => *FILE.lock().unwrap() = Some(file),
        Err(e) => eprintln!("Cannot open log file {}: {e}", path.display()),
    }
}

pub fn write(level: &str, args: std::fmt::Arguments) {
    let t = LocalTime::now();
    let line = format!(
        "{:04}-{:02}-{:02} {:02}:{:02}:{:02}.{:03} [{level}] {args}\n",
        t.year, t.month, t.day, t.hour, t.minute, t.second, t.millis
    );
    eprint!("{line}");
    if let Ok(mut file) = FILE.lock()
        && let Some(file) = file.as_mut()
    {
        let _ = file.write_all(line.as_bytes());
    }
}

macro_rules! info { ($($t:tt)*) => { $crate::log::write("INF", format_args!($($t)*)) } }
macro_rules! warning { ($($t:tt)*) => { $crate::log::write("WRN", format_args!($($t)*)) } }
macro_rules! error { ($($t:tt)*) => { $crate::log::write("ERR", format_args!($($t)*)) } }
pub(crate) use {error, info, warning};

struct LocalTime {
    year: i32,
    month: i32,
    day: i32,
    hour: i32,
    minute: i32,
    second: i32,
    millis: i64,
}

impl LocalTime {
    fn now() -> Self {
        // SAFETY: plain libc calls on stack-allocated, zero-initialised structs.
        unsafe {
            let mut ts: libc::timespec = std::mem::zeroed();
            libc::clock_gettime(libc::CLOCK_REALTIME, &mut ts);
            let mut tm: libc::tm = std::mem::zeroed();
            libc::localtime_r(&ts.tv_sec, &mut tm);
            Self {
                year: tm.tm_year + 1900,
                month: tm.tm_mon + 1,
                day: tm.tm_mday,
                hour: tm.tm_hour,
                minute: tm.tm_min,
                second: tm.tm_sec,
                millis: ts.tv_nsec / 1_000_000,
            }
        }
    }
}
