//! Console tab: every message in and out as a list, the selected one as indented JSON, and an input line that sends
//! events like the repl does (`<Event> [payload]`, `listen` / `unlisten`).

use std::collections::VecDeque;
use std::time::Instant;

use ratatui::Frame;
use ratatui::crossterm::event::{KeyCode, KeyEvent};
use ratatui::layout::{Constraint, Layout, Position, Rect};
use ratatui::style::{Color, Modifier, Style, Stylize};
use ratatui::text::{Line, Span};
use ratatui::widgets::{Block, List, ListItem, ListState, Paragraph};
use serde_json::Value;

use super::Effects;
use super::input::{InputAction, LineInput};
use crate::catalog::Catalog;
use crate::connection::Command;
use crate::line;

const MAX_ENTRIES: usize = 1000;
/// Pushed every second or so: hidden unless `f`.
const FREQUENT: [&str; 2] = ["DownloadStats", "DownloadList"];
const BUILTINS: [&str; 2] = ["listen", "unlisten"];

pub enum Direction {
    In,
    Out,
    Note,
}

struct Entry {
    seq: u64,
    at: f64,
    direction: Direction,
    kind: String,
    value: Value,
}

pub struct Console {
    started: Instant,
    entries: VecDeque<Entry>,
    seq: u64,
    show_frequent: bool,
    state: ListState,
    /// Keep the newest entry selected.
    follow: bool,
    detail: Option<(u64, Vec<Line<'static>>)>,
    scroll: u16,
    pub input: LineInput,
    pub input_focus: bool,
    hint: Option<String>,
}

impl Default for Console {
    fn default() -> Self {
        Self {
            started: Instant::now(),
            entries: VecDeque::new(),
            seq: 0,
            show_frequent: false,
            state: ListState::default(),
            follow: true,
            detail: None,
            scroll: 0,
            input: LineInput::default(),
            input_focus: true,
            hint: None,
        }
    }
}

impl Console {
    pub fn push(&mut self, direction: Direction, kind: &str, value: Value) {
        self.seq += 1;
        self.entries.push_back(Entry { seq: self.seq, at: self.started.elapsed().as_secs_f64(), direction, kind: kind.to_owned(), value });
        if self.entries.len() > MAX_ENTRIES {
            self.entries.pop_front();
            if let Some(i) = self.state.selected() {
                self.state.select(Some(i.saturating_sub(1)));
            }
        }
    }

    fn visible(&self) -> Vec<&Entry> {
        self.entries.iter().filter(|e| self.show_frequent || !FREQUENT.contains(&e.kind.as_str())).collect()
    }

    pub fn key(&mut self, key: KeyEvent, catalog: &Catalog, fx: &mut Effects) {
        if self.input_focus {
            match key.code {
                KeyCode::PageUp => self.scroll = self.scroll.saturating_sub(10),
                KeyCode::PageDown => self.scroll = self.scroll.saturating_add(10),
                _ => match self.input.key(key) {
                    InputAction::Submit(text) => self.submit(&text, catalog, fx),
                    InputAction::Cancel => self.input_focus = false,
                    InputAction::Complete => self.complete(catalog),
                    InputAction::None => self.hint = None,
                },
            }
            return;
        }
        let len = self.visible().len();
        let select = |delta: isize, this: &mut Self| {
            this.state.select(super::util::step(this.state.selected().or(len.checked_sub(1)), len, delta));
            this.follow = this.state.selected() == len.checked_sub(1);
            this.scroll = 0;
        };
        match key.code {
            KeyCode::Up | KeyCode::Char('k') => select(-1, self),
            KeyCode::Down | KeyCode::Char('j') => select(1, self),
            KeyCode::Home | KeyCode::Char('g') => select(-(len as isize), self),
            KeyCode::End | KeyCode::Char('G') => select(len as isize, self),
            KeyCode::PageUp => self.scroll = self.scroll.saturating_sub(10),
            KeyCode::PageDown | KeyCode::Char(' ') => self.scroll = self.scroll.saturating_add(10),
            KeyCode::Char('f') => {
                self.show_frequent = !self.show_frequent;
                self.follow = true;
                fx.info(if self.show_frequent { "Showing DownloadStats / DownloadList pushes" } else { "Hiding DownloadStats / DownloadList pushes" });
            }
            KeyCode::Char('c') => {
                self.entries.clear();
                self.state.select(None);
                self.follow = true;
            }
            KeyCode::Enter | KeyCode::Char('i') | KeyCode::Char(':') => self.input_focus = true,
            _ => {}
        }
    }

    fn submit(&mut self, text: &str, catalog: &Catalog, fx: &mut Effects) {
        self.hint = None;
        let words = match line::split_words(text.trim()) {
            Ok(w) if !w.is_empty() => w,
            Ok(_) => return,
            Err(e) => {
                self.hint = Some(e);
                return;
            }
        };
        let (head, rest) = words.split_first().expect("non-empty");
        if head.eq_ignore_ascii_case("listen") || head.eq_ignore_ascii_case("unlisten") {
            match line::subscription(rest) {
                Some(request) => {
                    let listen = head.eq_ignore_ascii_case("listen");
                    if listen && FREQUENT.iter().any(|t| request["topic"] == *t) {
                        self.show_frequent = true;
                    }
                    self.push(Direction::Out, if listen { "Subscribe" } else { "Unsubscribe" }, request.clone());
                    fx.commands.push(if listen { Command::Subscribe(request) } else { Command::Unsubscribe(request) });
                }
                None => self.hint = Some(format!("usage: {} <Topic> [key]", head.to_ascii_lowercase())),
            }
            self.follow = true;
            return;
        }
        let force = rest.iter().any(|a| a == "--force");
        let payload: Vec<String> = rest.iter().filter(|a| *a != "--force").cloned().collect();
        match crate::request(catalog, head, &payload, force) {
            Ok((name, payload)) => {
                fx.send(&name, payload);
                self.follow = true;
            }
            Err(e) => self.hint = Some(e),
        }
    }

    fn complete(&mut self, catalog: &Catalog) {
        let (start, candidates) = line::complete(catalog, &BUILTINS, self.input.before_cursor());
        match candidates.as_slice() {
            [] => self.hint = Some("no completions".into()),
            [one] => {
                let replacement = one.replacement.clone();
                self.input.replace_word(start, &replacement);
                self.hint = None;
            }
            many => {
                // Longest common prefix (case-insensitive matches all start with what was typed).
                let first = &many[0].replacement;
                let mut common = first.len();
                for c in &many[1..] {
                    common = common.min(first.chars().zip(c.replacement.chars()).take_while(|(a, b)| a == b).map(|(a, _)| a.len_utf8()).sum());
                }
                if common > self.input.before_cursor().len() - start {
                    let prefix = first[..common].to_owned();
                    self.input.replace_word(start, &prefix);
                }
                let names: Vec<&str> = many.iter().map(|c| c.display.as_str()).collect();
                self.hint = Some(names.join("  "));
            }
        }
    }

    pub fn render(&mut self, f: &mut Frame, area: Rect) {
        let [main, input_area] = Layout::vertical([Constraint::Min(3), Constraint::Length(3)]).areas(area);
        let [list_area, detail_area] = Layout::horizontal([Constraint::Percentage(40), Constraint::Percentage(60)]).areas(main);

        let visible_len = self.visible().len();
        if self.follow {
            self.state.select(visible_len.checked_sub(1));
        } else {
            self.state.select(super::util::clamp_selection(self.state.selected(), visible_len));
        }
        let items: Vec<ListItem> = self
            .visible()
            .iter()
            .map(|e| {
                let (arrow, style) = match e.direction {
                    Direction::In => ("←", Style::new().fg(Color::Green)),
                    Direction::Out => ("→", Style::new().fg(Color::Cyan)),
                    Direction::Note => ("#", Style::new().fg(Color::DarkGray)),
                };
                let is_error = e.kind == crate::ERROR_EVENT;
                ListItem::new(Line::from(vec![
                    Span::raw(format!("{:>7.1} ", e.at)).dark_gray(),
                    Span::styled(format!("{arrow} "), style),
                    Span::styled(e.kind.clone(), if is_error { Style::new().fg(Color::Red) } else { Style::new() }),
                    Span::raw(format!("  {}", summary(&e.value))).dark_gray(),
                ]))
            })
            .collect();
        let hidden = if self.show_frequent { String::new() } else { "  f: show stats pushes".into() };
        let mut block = Block::bordered().title(format!(" Messages ({visible_len}){hidden} "));
        if !self.input_focus {
            block = block.border_style(Style::new().fg(Color::Yellow));
        }
        let list = List::new(items).block(block).highlight_style(Style::new().add_modifier(Modifier::REVERSED));
        f.render_stateful_widget(list, list_area, &mut self.state);

        let selected = self.state.selected().and_then(|i| self.visible().get(i).map(|e| (e.seq, e.kind.clone())));
        let block = Block::bordered().title(selected.as_ref().map_or(" Payload ".into(), |(_, k)| format!(" {k} ")));
        if let Some((seq, _)) = selected {
            if self.detail.as_ref().map(|(s, _)| *s) != Some(seq) {
                let value = &self.visible()[self.state.selected().unwrap_or(0)].value;
                self.detail = Some((seq, highlight_json(value)));
            }
            let all = self.detail.as_ref().map_or(&[][..], |(_, l)| l.as_slice());
            let height = detail_area.height.saturating_sub(2) as usize;
            self.scroll = self.scroll.min(all.len().saturating_sub(height) as u16);
            // Only the visible window: a MediaList payload is thousands of lines.
            let window: Vec<Line> = all.iter().skip(self.scroll as usize).take(height).cloned().collect();
            let title_bottom = format!(" {}/{} PgUp/PgDn ", (self.scroll as usize + 1).min(all.len()), all.len());
            f.render_widget(Paragraph::new(window).block(block.title_bottom(Line::from(title_bottom).dark_gray())), detail_area);
        } else {
            f.render_widget(Paragraph::new("Nothing yet. Type an event below, e.g. MediaList, and press Enter.").dark_gray().block(block), detail_area);
        }

        let mut block = Block::bordered().title(" Send: <Event> [payload]   Tab completes · ↑↓ history · Esc leaves ");
        if let Some(hint) = &self.hint {
            block = block.title_bottom(Line::from(format!(" {hint} ")).yellow());
        }
        if self.input_focus {
            block = block.border_style(Style::new().fg(Color::Yellow));
        }
        let prompt = Line::from(vec![Span::raw("> ").dark_gray(), Span::raw(self.input.text.clone())]);
        f.render_widget(Paragraph::new(prompt).block(block), input_area);
        if self.input_focus {
            let x = input_area.x + 3 + self.input.before_cursor().chars().count() as u16;
            f.set_cursor_position(Position::new(x.min(input_area.right().saturating_sub(2)), input_area.y + 1));
        }
    }

    pub fn hints(&self) -> &'static str {
        if self.input_focus {
            "Enter send  Tab complete  ↑↓ history  PgUp/PgDn scroll payload  Esc leave input"
        } else {
            "↑↓ select  G follow  PgUp/PgDn scroll  f stats pushes  c clear  Enter/i type"
        }
    }
}

/// Short description of a payload for the list.
fn summary(v: &Value) -> String {
    match v {
        Value::Null => String::new(),
        Value::Array(a) => format!("[{}]", a.len()),
        Value::Object(o) if o.contains_key("bytes") && o.contains_key("params") => format!("{} bytes", o["bytes"]),
        Value::Object(o) => {
            let keys: Vec<&str> = o.keys().take(4).map(String::as_str).collect();
            format!("{{{}{}}}", keys.join(", "), if o.len() > 4 { ", …" } else { "" })
        }
        Value::String(s) if s.chars().count() > 40 => format!("\"{}…\"", s.chars().take(40).collect::<String>()),
        other => other.to_string(),
    }
}

/// Indented JSON with keys, strings and numbers coloured.
fn highlight_json(v: &Value) -> Vec<Line<'static>> {
    let text = serde_json::to_string_pretty(v).unwrap_or_default();
    text.lines()
        .map(|l| {
            let indent = l.len() - l.trim_start().len();
            let rest = &l[indent..];
            let mut spans = vec![Span::raw(l[..indent].to_owned())];
            let value = if let Some(after_key) = rest.strip_prefix('"').and_then(|r| key_end(r).map(|i| (i, r))) {
                let (i, r) = after_key;
                spans.push(Span::raw(format!("\"{}", &r[..=i])).fg(Color::Blue));
                spans.push(Span::raw(": "));
                &r[i + 3..]
            } else {
                rest
            };
            let trimmed = value.trim_end_matches(',');
            let style = match trimmed.chars().next() {
                Some('"') => Style::new().fg(Color::Green),
                Some('0'..='9' | '-') => Style::new().fg(Color::Cyan),
                Some('t' | 'f') => Style::new().fg(Color::Magenta),
                Some('n') => Style::new().fg(Color::DarkGray),
                _ => Style::new(),
            };
            spans.push(Span::styled(trimmed.to_owned(), style));
            if value.len() > trimmed.len() {
                spans.push(Span::raw(","));
            }
            Line::from(spans)
        })
        .collect()
}

/// In `key": value` (after the opening quote), the index of the key's closing quote when followed by `": `.
fn key_end(r: &str) -> Option<usize> {
    let mut escaped = false;
    for (i, c) in r.char_indices() {
        match c {
            '\\' if !escaped => escaped = true,
            '"' if !escaped => return r[i..].starts_with("\": ").then_some(i),
            _ => escaped = false,
        }
    }
    None
}
