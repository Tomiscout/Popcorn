//! Minimal logger: stderr + daily file in `<data>/logs/YYYY-MM-DD-Popcorn.Linux.txt` (the name the .NET apps use too,
//! see `Popcorn.Core/Shared/Logging/LogFileName.cs`), rolled over at local midnight.

use std::fs::{File, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

const APP: &str = "Popcorn.Linux";

struct State {
    dir: PathBuf,
    day: (u32, u32, u32),
    file: Option<(File, PathBuf)>,
}

static STATE: Mutex<Option<State>> = Mutex::new(None);

pub fn init(logs_dir: &Path) {
    let _ = std::fs::create_dir_all(logs_dir);
    let mut state = State {
        dir: logs_dir.to_path_buf(),
        day: (0, 0, 0),
        file: None,
    };
    state.open(&LocalTime::now());
    *STATE.lock().unwrap() = Some(state);
}

impl State {
    /// Appends (O_APPEND), so the installer's relaunched copy can share the day's file line by line. No file lock: the
    /// server's log viewer opens it with a shared one.
    fn open(&mut self, t: &LocalTime) {
        self.day = (t.year as u32, t.month as u32, t.day as u32);
        let path = self.dir.join(format!("{:04}-{:02}-{:02}-{APP}.txt", t.year, t.month, t.day));
        self.file = match OpenOptions::new().create(true).append(true).open(&path) {
            Ok(file) => Some((file, path)),
            Err(e) => {
                eprintln!("Cannot open log file {}: {e}", path.display());
                None
            }
        };
    }
}

pub fn write(level: &str, args: std::fmt::Arguments) {
    let t = LocalTime::now();
    let line = format!(
        "{:04}-{:02}-{:02} {:02}:{:02}:{:02}.{:03} [{level}] {args}\n",
        t.year, t.month, t.day, t.hour, t.minute, t.second, t.millis
    );
    eprint!("{line}");
    if let Ok(mut state) = STATE.lock()
        && let Some(state) = state.as_mut()
    {
        if state.day != (t.year as u32, t.month as u32, t.day as u32) {
            state.open(&t);
        }
        if let Some((file, _)) = state.file.as_mut() {
            let _ = file.write_all(line.as_bytes());
        }
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
