//! Library tab: items (MediaList) on the left, the selected item's files in play order (MediaItemFlatFileList) on
//! the right. Enter plays in the native player through the server (FileOpen).

use std::collections::{HashMap, HashSet};

use ratatui::Frame;
use ratatui::crossterm::event::{KeyCode, KeyEvent};
use ratatui::layout::{Constraint, Layout, Rect};
use ratatui::style::{Modifier, Style, Stylize};
use ratatui::text::{Line, Span};
use ratatui::widgets::{Block, List, ListItem, ListState, Paragraph};
use serde_json::{Value, json};

use super::util::{self, duration, num, opt_str, str_of};
use super::{Effects, Modal};
use crate::tui::input::LineInput;

pub struct Item {
    pub id: String,
    pub title: String,
    pub folder: String,
    /// Never played (MediaList `newItems`).
    pub new: bool,
    pub resume: Option<Resume>,
}

/// The file the item's card plays (MediaList `continue`).
pub struct Resume {
    pub file_id: String,
    pub label: String,
    pub position: Option<f64>,
    pub duration: Option<f64>,
}

pub struct FileRow {
    pub id: String,
    pub title: String,
    pub played: bool,
    pub position: Option<f64>,
    pub duration: Option<f64>,
}

#[derive(Default)]
pub struct Library {
    pub items: Vec<Item>,
    pub loaded: bool,
    pub filter: String,
    items_state: ListState,
    files: HashMap<String, Vec<FileRow>>,
    requested: HashSet<String>,
    files_state: ListState,
    focus_files: bool,
}

impl Library {
    pub fn refresh(&mut self, fx: &mut Effects) {
        self.files.clear();
        self.requested.clear();
        fx.send("MediaList", Value::Null);
    }

    fn visible(&self) -> Vec<&Item> {
        let filter = self.filter.to_lowercase();
        self.items.iter().filter(|i| filter.is_empty() || i.title.to_lowercase().contains(&filter)).collect()
    }

    fn selected(&self) -> Option<&Item> {
        self.items_state.selected().and_then(|i| self.visible().get(i).copied())
    }

    fn selected_files(&self) -> Option<&Vec<FileRow>> {
        self.selected().and_then(|i| self.files.get(&i.id))
    }

    pub fn on_media_list(&mut self, payload: &Value) {
        let keep = self.selected().map(|i| i.id.clone());
        let resume = payload.get("continue");
        let list = |key: &str, new: bool| {
            payload.get(key).and_then(Value::as_array).cloned().unwrap_or_default().into_iter().map(move |v| (v, new))
        };
        self.items = list("recentItems", false)
            .chain(list("newItems", true))
            .map(|(v, new)| {
                let id = str_of(&v, "id");
                let resume = resume.and_then(|c| c.get(&id)).map(|c| Resume {
                    file_id: str_of(c, "fileId"),
                    label: str_of(c, "label"),
                    position: num(c, "position"),
                    duration: num(c, "duration"),
                });
                Item {
                    title: opt_str(&v, "title").unwrap_or_else(|| str_of(&v, "shortTitle")),
                    folder: str_of(&v, "folderDirectory"),
                    id,
                    new,
                    resume,
                }
            })
            .collect();
        self.loaded = true;
        let index = keep.and_then(|id| self.visible().iter().position(|i| i.id == id));
        self.items_state.select(index.or_else(|| util::clamp_selection(self.items_state.selected(), self.visible().len())));
    }

    pub fn on_flat_files(&mut self, payload: &Value) {
        let item_id = str_of(payload, "itemId");
        let prefix = self.items.iter().find(|i| i.id == item_id).map(|i| i.title.clone());
        let rows = payload
            .get("files")
            .and_then(Value::as_array)
            .map(|files| {
                files
                    .iter()
                    .map(|f| {
                        let mut title = opt_str(f, "shortTitle").unwrap_or_else(|| str_of(f, "title"));
                        if let Some(rest) = prefix.as_deref().and_then(|p| strip_title(&title, p)) {
                            title = rest.to_owned();
                        }
                        FileRow {
                            id: str_of(f, "id"),
                            title,
                            played: util::flag(f, "played"),
                            position: num(f, "playbackPosition"),
                            duration: num(f, "duration"),
                        }
                    })
                    .collect()
            })
            .unwrap_or_default();
        self.files.insert(item_id, rows);
        if self.focus_files {
            self.files_state.select(util::clamp_selection(self.files_state.selected(), self.selected_files().map_or(0, Vec::len)));
        }
    }

    /// Asks for the selected item's files once (MediaItem answers with MediaItemFlatFileList among others).
    pub fn request_files(&mut self, fx: &mut Effects) {
        let Some(id) = self.selected().map(|i| i.id.clone()) else { return };
        if !self.files.contains_key(&id) && self.requested.insert(id.clone()) {
            fx.send_quiet("MediaItem", json!({ "id": id }));
        }
    }

    pub fn open_filter(&self) -> Modal {
        Modal::Filter(LineInput::with_text(&self.filter))
    }

    pub fn set_filter(&mut self, filter: &str) {
        self.filter = filter.to_owned();
        self.items_state.select(util::clamp_selection(self.items_state.selected(), self.visible().len()));
        self.focus_files = false;
    }

    pub fn key(&mut self, key: KeyEvent, fx: &mut Effects) {
        let page = 10;
        let items_len = self.visible().len();
        let files_len = self.selected_files().map_or(0, Vec::len);
        let (state, len) = if self.focus_files { (&mut self.files_state, files_len) } else { (&mut self.items_state, items_len) };
        match key.code {
            KeyCode::Up | KeyCode::Char('k') => util::step_list(state, len, -1),
            KeyCode::Down | KeyCode::Char('j') => util::step_list(state, len, 1),
            KeyCode::PageUp => util::step_list(state, len, -page),
            KeyCode::PageDown => util::step_list(state, len, page),
            KeyCode::Home | KeyCode::Char('g') => util::step_list(state, len, -(len as isize)),
            KeyCode::End | KeyCode::Char('G') => util::step_list(state, len, len as isize),
            KeyCode::Right | KeyCode::Char('l') if !self.focus_files && files_len > 0 => {
                self.focus_files = true;
                let resume = self.selected().and_then(|i| i.resume.as_ref()).map(|r| r.file_id.clone());
                let at = self.selected_files().and_then(|f| f.iter().position(|f| Some(&f.id) == resume.as_ref()));
                self.files_state.select(at.or(Some(0)));
            }
            KeyCode::Left | KeyCode::Char('h') | KeyCode::Esc if self.focus_files => self.focus_files = false,
            KeyCode::Enter => self.play(fx),
            KeyCode::Char('/') => fx.modal = Some(self.open_filter()),
            KeyCode::Char('R') => {
                if let Some(item) = self.selected() {
                    let id = item.id.clone();
                    fx.send("RefreshMediaItem", json!(id));
                    fx.info(format!("Rescanning {}", item.title));
                    self.files.remove(&id);
                    self.requested.remove(&id);
                }
            }
            _ => {}
        }
        if !self.focus_files {
            self.request_files(fx);
        }
    }

    fn play(&self, fx: &mut Effects) {
        let Some(item) = self.selected() else { return };
        let files = self.files.get(&item.id);
        let (file_id, label) = if self.focus_files {
            match self.files_state.selected().and_then(|i| files?.get(i)) {
                Some(f) => (f.id.clone(), f.title.clone()),
                None => return,
            }
        } else if let Some(r) = &item.resume {
            (r.file_id.clone(), r.label.clone())
        } else if let Some(f) = files.and_then(|f| f.first()) {
            (f.id.clone(), f.title.clone())
        } else {
            fx.error(format!("{} has no files to play", item.title));
            return;
        };
        fx.send("FileOpen", json!(file_id));
        fx.info(format!("Playing {} {label}", item.title));
    }

    pub fn render(&mut self, f: &mut Frame, area: Rect) {
        let [left, right] = Layout::horizontal([Constraint::Percentage(40), Constraint::Percentage(60)]).areas(area);
        let focused = Style::new().fg(ratatui::style::Color::Yellow);
        let title = if self.filter.is_empty() {
            format!(" Library ({}) ", self.items.len())
        } else {
            format!(" Library: \"{}\" ({}/{}) ", self.filter, self.visible().len(), self.items.len())
        };

        let rows: Vec<ListItem> = self
            .visible()
            .iter()
            .map(|i| {
                let mut spans = vec![Span::raw(i.title.clone())];
                if i.new {
                    spans.push(Span::raw("  new").green());
                } else if let Some(r) = &i.resume {
                    if !r.label.is_empty() {
                        spans.push(Span::raw(format!("  {}", r.label)).dark_gray());
                    }
                    if let (Some(p), Some(d)) = (r.position, r.duration)
                        && d > 0.0
                    {
                        spans.push(Span::raw(format!(" {}%", (p / d * 100.0).round())).dark_gray());
                    }
                }
                ListItem::new(Line::from(spans))
            })
            .collect();
        let mut block = Block::bordered().title(title);
        if !self.focus_files {
            block = block.border_style(focused);
        }
        let empty = rows.is_empty();
        let list = List::new(rows)
            .block(block)
            .highlight_style(Style::new().add_modifier(Modifier::REVERSED))
            .highlight_symbol("▶ ");
        if empty {
            let text = if self.loaded { "No items" } else { "Loading…" };
            f.render_widget(Paragraph::new(text).dark_gray().block(Block::bordered().title(" Library ")), left);
        } else {
            f.render_stateful_widget(list, left, &mut self.items_state);
        }

        let Some(item) = self.selected() else {
            f.render_widget(Block::bordered(), right);
            return;
        };
        let mut block = Block::bordered().title(format!(" {} ", item.title)).title_bottom(Line::from(format!(" {} ", item.folder)).dark_gray());
        if self.focus_files {
            block = block.border_style(focused);
        }
        let resume_id = item.resume.as_ref().map(|r| r.file_id.as_str());
        let Some(files) = self.files.get(&item.id) else {
            f.render_widget(Paragraph::new("Loading…").dark_gray().block(block), right);
            return;
        };
        let rows: Vec<ListItem> = files
            .iter()
            .map(|file| {
                let mark = if Some(file.id.as_str()) == resume_id {
                    Span::raw("▸ ").yellow()
                } else if file.played {
                    Span::raw("✓ ").green()
                } else {
                    Span::raw("  ")
                };
                let mut spans = vec![mark, Span::raw(file.title.clone())];
                match (file.position, file.duration) {
                    (Some(p), Some(d)) if p > 1.0 && !file.played => {
                        spans.push(Span::raw(format!("  {} / {}", duration(p), duration(d))).dark_gray())
                    }
                    (_, Some(d)) => spans.push(Span::raw(format!("  {}", duration(d))).dark_gray()),
                    _ => {}
                }
                ListItem::new(Line::from(spans))
            })
            .collect();
        if rows.is_empty() {
            f.render_widget(Paragraph::new("No files").dark_gray().block(block), right);
            return;
        }
        let list = List::new(rows).block(block).highlight_style(if self.focus_files {
            Style::new().add_modifier(Modifier::REVERSED)
        } else {
            Style::new()
        });
        let mut state = if self.focus_files { self.files_state } else { ListState::default() };
        f.render_stateful_widget(list, right, &mut state);
        if self.focus_files {
            self.files_state = state;
        }
    }

    pub fn hints(&self) -> &'static str {
        if self.focus_files {
            "↑↓ move  Enter play  ← items  R rescan  r refresh"
        } else {
            "↑↓ move  Enter play/continue  → files  / filter  R rescan  r refresh"
        }
    }
}

/// "The Legend of Korra S04E01" under "The Legend of Korra" → "S04E01". Compares letters and digits only
/// ("Avatar Seven Havens S01E01" under "Avatar: Seven Havens"); None when the title doesn't start with the item's.
fn strip_title<'a>(title: &'a str, item: &str) -> Option<&'a str> {
    let mut want = item.chars().filter(|c| c.is_alphanumeric()).flat_map(char::to_lowercase).peekable();
    want.peek()?;
    let mut end = 0;
    for (i, c) in title.char_indices() {
        if want.peek().is_none() {
            break;
        }
        end = i + c.len_utf8();
        if !c.is_alphanumeric() {
            continue;
        }
        if c.to_lowercase().ne(want.next()) {
            return None;
        }
    }
    let rest = title[end..].trim_start_matches(|c: char| !c.is_alphanumeric());
    (want.peek().is_none() && !rest.is_empty()).then_some(rest)
}

#[cfg(test)]
mod tests {
    use super::strip_title;

    #[test]
    fn strips_item_title() {
        assert_eq!(strip_title("The Legend of Korra S04E01", "The Legend of Korra"), Some("S04E01"));
        assert_eq!(strip_title("Avatar Seven Havens S01E01", "Avatar: Seven Havens"), Some("S01E01"));
        assert_eq!(strip_title("Shrek", "Shrek"), None);
        assert_eq!(strip_title("Other S01E01", "Avatar"), None);
    }
}
