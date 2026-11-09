//! Minimal logger (same format as Popcorn.Linux): stderr + daily file in `<data>/logs/YYYY-MM-DD-Popcorn.Installer.txt`
//! (the name the .NET apps use too, see `Popcorn.Core/Shared/Logging/LogFileName.cs`), rolled over at local midnight.

use std::fs::{File, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

const APP: &str = "Popcorn.Installer";

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

/// The file the log goes to, for error messages.
pub fn file_path() -> Option<PathBuf> {
    STATE.lock().ok()?.as_ref()?.file.as_ref().map(|(_, path)| path.clone())
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
    year: u32,
    month: u32,
    day: u32,
    hour: u32,
    minute: u32,
    second: u32,
    millis: u32,
}

impl LocalTime {
    #[cfg(windows)]
    fn now() -> Self {
        // SAFETY: fills a stack-allocated SYSTEMTIME.
        let t = unsafe {
            let mut t = std::mem::zeroed();
            windows_sys::Win32::System::SystemInformation::GetLocalTime(&mut t);
            t
        };
        Self {
            year: t.wYear.into(),
            month: t.wMonth.into(),
            day: t.wDay.into(),
            hour: t.wHour.into(),
            minute: t.wMinute.into(),
            second: t.wSecond.into(),
            millis: t.wMilliseconds.into(),
        }
    }

    #[cfg(target_os = "linux")]
    fn now() -> Self {
        let since_epoch = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap_or_default();
        let secs = since_epoch.as_secs() as libc::time_t;
        // SAFETY: localtime_r fills the zeroed struct; both pointers are to locals.
        let t = unsafe {
            let mut t: libc::tm = std::mem::zeroed();
            libc::localtime_r(&secs, &mut t);
            t
        };
        Self {
            year: (t.tm_year + 1900) as u32,
            month: (t.tm_mon + 1) as u32,
            day: t.tm_mday as u32,
            hour: t.tm_hour as u32,
            minute: t.tm_min as u32,
            second: t.tm_sec as u32,
            millis: since_epoch.subsec_millis(),
        }
    }

    /// UTC elsewhere.
    #[cfg(not(any(windows, target_os = "linux")))]
    fn now() -> Self {
        let since_epoch = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap_or_default();
        let secs = since_epoch.as_secs();
        // Days since 1970-01-01 to a civil date (Howard Hinnant's days_from_civil, inverted).
        let z = (secs / 86_400) as i64 + 719_468;
        let era = z.div_euclid(146_097);
        let doe = z - era * 146_097;
        let yoe = (doe - doe / 1_460 + doe / 36_524 - doe / 146_096) / 365;
        let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        let mp = (5 * doy + 2) / 153;
        let day = (doy - (153 * mp + 2) / 5 + 1) as u32;
        let month = if mp < 10 { mp + 3 } else { mp - 9 } as u32;
        let year = (yoe + era * 400 + i64::from(month <= 2)) as u32;
        Self {
            year,
            month,
            day,
            hour: (secs / 3_600 % 24) as u32,
            minute: (secs / 60 % 60) as u32,
            second: (secs % 60) as u32,
            millis: since_epoch.subsec_millis(),
        }
    }
}
