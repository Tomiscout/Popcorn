//! Downloads tab: the DownloadList subscription as a table, the selected download's details and files below.

use ratatui::Frame;
use ratatui::crossterm::event::{KeyCode, KeyEvent};
use ratatui::layout::{Constraint, Layout, Rect};
use ratatui::style::{Color, Modifier, Style, Stylize};
use ratatui::text::{Line, Span};
use ratatui::widgets::{Block, Cell, Paragraph, Row, Table, TableState};
use serde_json::{Value, json};

use super::util::{self, bar, bytes, duration, flag, num, opt_str, rate, str_of};
use super::{Effects, Modal};
use crate::connection::Command;
use crate::tui::input::LineInput;

pub struct Download {
    pub id: String,
    pub name: String,
    pub state: String,
    pub error: Option<String>,
    pub save_path: String,
    pub size: f64,
    pub downloaded: f64,
    pub progress: f64,
    pub down: f64,
    pub up: f64,
    pub ratio: f64,
    pub eta: Option<f64>,
    pub peers: f64,
    pub seeds: f64,
    pub item: Option<String>,
    pub streaming: bool,
    pub files: Vec<DownloadFile>,
}

pub struct DownloadFile {
    pub path: String,
    pub length: f64,
    pub progress: f64,
    pub wanted: bool,
    pub video: bool,
}

#[derive(Default)]
pub struct Downloads {
    pub list: Vec<Download>,
    pub loaded: bool,
    /// DownloadStats: down, up, active.
    pub stats: Option<(f64, f64, f64)>,
    state: TableState,
}

impl Downloads {
    pub fn on_list(&mut self, payload: &Value) {
        let keep = self.selected().map(|d| d.id.clone());
        self.list = payload
            .as_array()
            .map(|a| {
                a.iter()
                    .map(|d| Download {
                        id: str_of(d, "id"),
                        name: opt_str(d, "displayName").unwrap_or_else(|| str_of(d, "name")),
                        state: str_of(d, "state"),
                        error: opt_str(d, "error"),
                        save_path: str_of(d, "savePath"),
                        size: num(d, "size").unwrap_or(-1.0),
                        downloaded: num(d, "downloaded").unwrap_or(0.0),
                        progress: num(d, "progress").unwrap_or(0.0),
                        down: num(d, "downloadRate").unwrap_or(0.0),
                        up: num(d, "uploadRate").unwrap_or(0.0),
                        ratio: num(d, "ratio").unwrap_or(0.0),
                        eta: num(d, "eta"),
                        peers: num(d, "peers").unwrap_or(0.0),
                        seeds: num(d, "seeds").unwrap_or(0.0),
                        item: opt_str(d, "mediaItemTitle"),
                        streaming: flag(d, "streaming"),
                        files: d
                            .get("files")
                            .and_then(Value::as_array)
                            .map(|files| {
                                files
                                    .iter()
                                    .map(|f| DownloadFile {
                                        path: str_of(f, "path"),
                                        length: num(f, "length").unwrap_or(0.0),
                                        progress: num(f, "progress").unwrap_or(0.0),
                                        wanted: f.get("wanted").and_then(Value::as_bool).unwrap_or(true),
                                        video: flag(f, "isVideo"),
                                    })
                                    .collect()
                            })
                            .unwrap_or_default(),
                    })
                    .collect()
            })
            .unwrap_or_default();
        self.loaded = true;
        let index = keep.and_then(|id| self.list.iter().position(|d| d.id == id));
        self.state.select(index.or_else(|| util::clamp_selection(self.state.selected(), self.list.len())));
    }

    pub fn on_stats(&mut self, payload: &Value) {
        self.stats = Some((
            num(payload, "downloadRate").unwrap_or(0.0),
            num(payload, "uploadRate").unwrap_or(0.0),
            num(payload, "active").unwrap_or(0.0),
        ));
    }

    fn selected(&self) -> Option<&Download> {
        self.state.selected().and_then(|i| self.list.get(i))
    }

    pub fn key(&mut self, key: KeyEvent, fx: &mut Effects) {
        let len = self.list.len();
        match key.code {
            KeyCode::Up | KeyCode::Char('k') => util::step_table(&mut self.state, len, -1),
            KeyCode::Down | KeyCode::Char('j') => util::step_table(&mut self.state, len, 1),
            KeyCode::Home | KeyCode::Char('g') => util::step_table(&mut self.state, len, -(len as isize)),
            KeyCode::End | KeyCode::Char('G') => util::step_table(&mut self.state, len, len as isize),
            KeyCode::Char('a') => fx.modal = Some(Modal::AddDownload(LineInput::default())),
            _ => {}
        }
        let Some(d) = self.selected() else { return };
        let id = json!({ "id": d.id });
        match key.code {
            KeyCode::Char(' ') | KeyCode::Char('p') => {
                if d.state == "Paused" {
                    fx.send("DownloadResume", id);
                    fx.info(format!("Resuming {}", d.name));
                } else {
                    fx.send("DownloadPause", id);
                    fx.info(format!("Pausing {}", d.name));
                }
            }
            KeyCode::Char('x') | KeyCode::Delete => {
                fx.modal = Some(Modal::Confirm {
                    text: format!("Remove \"{}\" (keep the files)?", d.name),
                    command: Command::Send("DownloadRemove".into(), json!({ "id": d.id, "deleteFiles": false })),
                })
            }
            KeyCode::Char('X') => {
                fx.modal = Some(Modal::Confirm {
                    text: format!("Remove \"{}\" AND DELETE its files from disk?", d.name),
                    command: Command::Send("DownloadRemove".into(), json!({ "id": d.id, "deleteFiles": true })),
                })
            }
            _ => {}
        }
    }

    pub fn render(&mut self, f: &mut Frame, area: Rect) {
        let [top, bottom] = Layout::vertical([Constraint::Percentage(55), Constraint::Percentage(45)]).areas(area);
        let header = Row::new(["Name", "State", "Progress", "↓", "↑", "ETA", "Size", "Peers"]).bold();
        let rows: Vec<Row> = self
            .list
            .iter()
            .map(|d| {
                let state_style = match d.state.as_str() {
                    "Downloading" | "Metadata" | "Hashing" => Style::new().fg(Color::Cyan),
                    "Seeding" | "Completed" => Style::new().fg(Color::Green),
                    "Paused" => Style::new().fg(Color::DarkGray),
                    "Error" => Style::new().fg(Color::Red),
                    _ => Style::new(),
                };
                let state = if d.streaming { format!("{} ▶", d.state) } else { d.state.clone() };
                Row::new([
                    Cell::from(d.name.clone()),
                    Cell::from(state).style(state_style),
                    Cell::from(format!("{} {:>3.0}%", bar(d.progress, 10), d.progress * 100.0)),
                    Cell::from(rate(d.down)),
                    Cell::from(rate(d.up)),
                    Cell::from(d.eta.filter(|_| d.down > 0.0).map(duration).unwrap_or_else(|| "-".into())),
                    Cell::from(bytes(d.size)),
                    Cell::from(format!("{:.0}/{:.0}", d.seeds, d.peers)),
                ])
            })
            .collect();
        let title = match self.stats {
            Some((down, up, active)) => format!(" Downloads ({})  ↓ {}  ↑ {}  {active:.0} active ", self.list.len(), rate(down), rate(up)),
            None => format!(" Downloads ({}) ", self.list.len()),
        };
        let block = Block::bordered().title(title).border_style(Style::new().fg(Color::Yellow));
        if rows.is_empty() {
            let text = if self.loaded { "No downloads. `a` adds a magnet link or URL." } else { "Loading…" };
            f.render_widget(Paragraph::new(text).dark_gray().block(block), top);
        } else {
            let table = Table::new(
                rows,
                [
                    Constraint::Fill(1),
                    Constraint::Length(13),
                    Constraint::Length(15),
                    Constraint::Length(11),
                    Constraint::Length(11),
                    Constraint::Length(8),
                    Constraint::Length(9),
                    Constraint::Length(7),
                ],
            )
            .header(header)
            .block(block)
            .row_highlight_style(Style::new().add_modifier(Modifier::REVERSED));
            f.render_stateful_widget(table, top, &mut self.state);
        }

        let Some(d) = self.selected() else {
            f.render_widget(Block::bordered().title(" Details "), bottom);
            return;
        };
        let mut lines = vec![
            Line::from(vec![Span::raw("Folder  ").dark_gray(), Span::raw(d.save_path.clone())]),
            Line::from(vec![
                Span::raw("Done    ").dark_gray(),
                Span::raw(format!("{} of {}  ratio {:.2}", bytes(d.downloaded), bytes(d.size), d.ratio)),
            ]),
        ];
        if let Some(item) = &d.item {
            lines.push(Line::from(vec![Span::raw("Library ").dark_gray(), Span::raw(item.clone())]));
        }
        if let Some(e) = &d.error {
            lines.push(Line::from(vec![Span::raw("Error   ").dark_gray(), Span::raw(e.clone()).red()]));
        }
        lines.push(Line::raw(""));
        let mut files: Vec<&DownloadFile> = d.files.iter().collect();
        files.sort_by_key(|f| !f.video);
        for file in files {
            let style = if !file.wanted { Style::new().fg(Color::DarkGray) } else if file.video { Style::new() } else { Style::new().fg(Color::Gray) };
            lines.push(Line::from(vec![
                Span::raw(format!("{} {:>3.0}%  {:>9}  ", bar(file.progress, 8), file.progress * 100.0, bytes(file.length))).dark_gray(),
                Span::styled(if file.wanted { file.path.clone() } else { format!("{} (skipped)", file.path) }, style),
            ]));
        }
        f.render_widget(Paragraph::new(lines).block(Block::bordered().title(format!(" {} ", d.name))), bottom);
    }

    pub fn hints(&self) -> &'static str {
        "↑↓ move  Space pause/resume  a add  x remove  X remove + delete files"
    }
}
