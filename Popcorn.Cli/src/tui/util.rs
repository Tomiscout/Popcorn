//! Formatting and JSON field helpers shared by the tabs.

use ratatui::widgets::{ListState, TableState};
use serde_json::Value;

pub fn str_of(v: &Value, key: &str) -> String {
    v.get(key).and_then(Value::as_str).unwrap_or_default().to_owned()
}

pub fn opt_str(v: &Value, key: &str) -> Option<String> {
    v.get(key).and_then(Value::as_str).filter(|s| !s.is_empty()).map(str::to_owned)
}

pub fn num(v: &Value, key: &str) -> Option<f64> {
    v.get(key).and_then(Value::as_f64)
}

pub fn flag(v: &Value, key: &str) -> bool {
    v.get(key).and_then(Value::as_bool).unwrap_or(false)
}

/// 1370.4 → "22:50", 4000 → "1:06:40".
pub fn duration(secs: f64) -> String {
    let s = secs.max(0.0) as u64;
    let (h, m, s) = (s / 3600, s / 60 % 60, s % 60);
    if h > 0 { format!("{h}:{m:02}:{s:02}") } else { format!("{m}:{s:02}") }
}

pub fn bytes(n: f64) -> String {
    if n < 0.0 {
        return "?".into();
    }
    const UNITS: [&str; 5] = ["B", "KB", "MB", "GB", "TB"];
    let mut v = n;
    let mut unit = 0;
    while v >= 1000.0 && unit < UNITS.len() - 1 {
        v /= 1024.0;
        unit += 1;
    }
    if unit == 0 { format!("{v:.0} B") } else { format!("{v:.1} {}", UNITS[unit]) }
}

pub fn rate(n: f64) -> String {
    if n <= 0.0 { "-".into() } else { format!("{}/s", bytes(n)) }
}

/// `width` cells of █/░ for a 0..1 fraction.
pub fn bar(fraction: f64, width: usize) -> String {
    let filled = ((fraction.clamp(0.0, 1.0) * width as f64).round() as usize).min(width);
    format!("{}{}", "█".repeat(filled), "░".repeat(width - filled))
}

/// Moves a list selection by `delta` (clamped), selecting the first entry when nothing is.
pub fn step(selected: Option<usize>, len: usize, delta: isize) -> Option<usize> {
    if len == 0 {
        return None;
    }
    let current = selected.unwrap_or(0) as isize;
    Some((current + delta).clamp(0, len as isize - 1) as usize)
}

pub fn step_list(state: &mut ListState, len: usize, delta: isize) {
    state.select(step(state.selected(), len, delta));
}

pub fn step_table(state: &mut TableState, len: usize, delta: isize) {
    state.select(step(state.selected(), len, delta));
}

/// Keeps a selection inside a list that changed length.
pub fn clamp_selection(selected: Option<usize>, len: usize) -> Option<usize> {
    match (selected, len) {
        (_, 0) => None,
        (None, _) => Some(0),
        (Some(i), n) => Some(i.min(n - 1)),
    }
}
