//! `popcorn-cli` without a command in a terminal: full-screen UI with Library, Downloads and Console tabs. Talks to
//! the server through the same background connection as the repl (`connection.rs`), so it reconnects by itself.

mod console;
mod downloads;
mod input;
mod library;
mod util;

use std::io;
use std::process::ExitCode;
use std::sync::mpsc::{self, Receiver, Sender};
use std::time::{Duration, Instant};

use ratatui::crossterm::event::{self, Event as TermEvent, KeyCode, KeyEvent, KeyEventKind, KeyModifiers};
use ratatui::layout::{Constraint, Flex, Layout, Position, Rect};
use ratatui::style::{Color, Modifier, Style, Stylize};
use ratatui::text::{Line, Span};
use ratatui::widgets::{Block, Clear, Paragraph, Tabs, Wrap};
use ratatui::{DefaultTerminal, Frame};
use serde_json::{Value, json};

use crate::catalog::Catalog;
use crate::client::Incoming;
use crate::connection::{self, Command, Event};
use console::{Console, Direction};
use downloads::Downloads;
use input::{InputAction, LineInput};
use library::Library;

#[derive(Clone, Copy, PartialEq)]
enum Tab {
    Library,
    Downloads,
    Console,
}

const TABS: [Tab; 3] = [Tab::Library, Tab::Downloads, Tab::Console];

pub enum Modal {
    Filter(LineInput),
    AddDownload(LineInput),
    Confirm { text: String, command: Command },
    Help,
}

/// What a tab's key handler wants done: messages to send, a status line, a dialog.
#[derive(Default)]
pub struct Effects {
    commands: Vec<Command>,
    /// Sent quietly: not listed in the console (requests the UI makes on its own, e.g. while browsing).
    quiet: Vec<bool>,
    status: Option<(String, bool)>,
    modal: Option<Modal>,
}

impl Effects {
    pub fn send(&mut self, name: &str, payload: Value) {
        self.commands.push(Command::Send(name.to_owned(), payload));
        self.quiet.push(false);
    }

    pub fn send_quiet(&mut self, name: &str, payload: Value) {
        self.commands.push(Command::Send(name.to_owned(), payload));
        self.quiet.push(true);
    }

    pub fn info(&mut self, text: impl Into<String>) {
        self.status = Some((text.into(), false));
    }

    pub fn error(&mut self, text: impl Into<String>) {
        self.status = Some((text.into(), true));
    }
}

struct App {
    catalog: Catalog,
    server: String,
    commands: Sender<Command>,
    tab: Tab,
    connected: Option<bool>,
    status: Option<(String, bool, Instant)>,
    modal: Option<Modal>,
    library: Library,
    downloads: Downloads,
    console: Console,
    quit: bool,
}

pub fn run(catalog: Catalog, server: String) -> ExitCode {
    let (cmd_tx, cmd_rx) = mpsc::channel::<Command>();
    let (event_tx, event_rx) = mpsc::channel::<Event>();
    let socket = connection::spawn(server.clone(), cmd_rx, move |e| {
        let _ = event_tx.send(e);
    });
    // The lists the tabs show; resent after every reconnect by the connection.
    for topic in ["DownloadList", "DownloadStats"] {
        let _ = cmd_tx.send(Command::Subscribe(json!({ "topic": topic, "key": null })));
    }

    let mut app = App {
        catalog,
        server,
        commands: cmd_tx,
        tab: Tab::Library,
        connected: None,
        status: None,
        modal: None,
        library: Library::default(),
        downloads: Downloads::default(),
        console: Console::default(),
        quit: false,
    };

    let mut terminal = ratatui::init();
    let result = app.event_loop(&mut terminal, &event_rx);
    ratatui::restore();

    let _ = app.commands.send(Command::Close);
    drop(app);
    let _ = socket.join();
    match result {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("popcorn-cli: {e}");
            ExitCode::from(1)
        }
    }
}

impl App {
    fn event_loop(&mut self, terminal: &mut DefaultTerminal, events: &Receiver<Event>) -> io::Result<()> {
        while !self.quit {
            terminal.draw(|f| self.render(f))?;
            if event::poll(Duration::from_millis(50))? {
                // Everything queued, so a key repeat or paste doesn't draw once per key.
                loop {
                    if let TermEvent::Key(key) = event::read()?
                        && key.kind != KeyEventKind::Release
                    {
                        self.key(key);
                    }
                    if self.quit || !event::poll(Duration::ZERO)? {
                        break;
                    }
                }
            }
            while let Ok(e) = events.try_recv() {
                self.connection_event(e);
            }
        }
        Ok(())
    }

    fn apply(&mut self, fx: Effects) {
        for (cmd, quiet) in fx.commands.into_iter().zip(fx.quiet.into_iter().chain(std::iter::repeat(false))) {
            if let Command::Send(name, payload) = &cmd
                && !quiet
            {
                self.console.push(Direction::Out, name, payload.clone());
            }
            let _ = self.commands.send(cmd);
        }
        if let Some((text, error)) = fx.status {
            self.status = Some((text, error, Instant::now()));
        }
        if fx.modal.is_some() {
            self.modal = fx.modal;
        }
    }

    fn connection_event(&mut self, e: Event) {
        match e {
            Event::Connected => {
                if self.connected == Some(false) {
                    self.status = Some(("Connected".into(), false, Instant::now()));
                }
                self.connected = Some(true);
                self.console.push(Direction::Note, "connected", json!(self.server));
                let mut fx = Effects::default();
                self.library.refresh(&mut fx);
                self.apply(fx);
            }
            Event::Down(reason) => {
                self.connected = Some(false);
                self.console.push(Direction::Note, "disconnected", json!(reason));
                self.status = Some((format!("{reason}; retrying every 2 s"), true, Instant::now()));
            }
            Event::Message(Incoming::Event { kind, payload }) => {
                let mut fx = Effects::default();
                match kind.as_str() {
                    "MediaList" => {
                        self.library.on_media_list(&payload);
                        self.library.request_files(&mut fx);
                    }
                    "MediaItemFlatFileList" => self.library.on_flat_files(&payload),
                    "DownloadList" => self.downloads.on_list(&payload),
                    "DownloadStats" => self.downloads.on_stats(&payload),
                    "DownloadAdded" => match payload.get("error").and_then(Value::as_str) {
                        Some(e) => fx.error(format!("Download not added: {e}")),
                        None => fx.info(format!("Added {}", util::str_of(&payload, "name"))),
                    },
                    crate::ERROR_EVENT => fx.error(format!("The server failed to handle {} (see its log)", payload.as_str().unwrap_or("?"))),
                    _ => {}
                }
                self.console.push(Direction::In, &kind, payload);
                self.apply(fx);
            }
            Event::Message(Incoming::Binary { kind, params, data }) => {
                self.console.push(Direction::In, &kind, json!({ "params": params, "bytes": data.len() }));
            }
        }
    }

    fn key(&mut self, key: KeyEvent) {
        let ctrl = key.modifiers.contains(KeyModifiers::CONTROL);
        let alt = key.modifiers.contains(KeyModifiers::ALT);
        if ctrl && key.code == KeyCode::Char('c') {
            self.quit = true;
            return;
        }
        if let Some(modal) = self.modal.take() {
            self.modal_key(modal, key);
            return;
        }
        // Tab switching that works even while typing in the console.
        let direct = match key.code {
            KeyCode::F(n @ 1..=3) => Some(n as usize - 1),
            KeyCode::Char(c @ '1'..='3') if alt => Some(c as usize - '1' as usize),
            _ => None,
        };
        if let Some(i) = direct {
            self.tab = TABS[i];
            return;
        }
        let mut fx = Effects::default();
        if self.tab == Tab::Console && self.console.input_focus {
            self.console.key(key, &self.catalog, &mut fx);
            self.apply(fx);
            return;
        }
        match key.code {
            KeyCode::Char('q') => self.quit = true,
            KeyCode::Char(c @ '1'..='3') => self.tab = TABS[c as usize - '1' as usize],
            KeyCode::Tab => self.tab = TABS[(self.tab_index() + 1) % TABS.len()],
            KeyCode::BackTab => self.tab = TABS[(self.tab_index() + TABS.len() - 1) % TABS.len()],
            KeyCode::Char('?') => self.modal = Some(Modal::Help),
            KeyCode::Char('r') => {
                self.library.refresh(&mut fx);
                fx.send_quiet("DownloadList", Value::Null);
                fx.info("Refreshed");
            }
            _ => match self.tab {
                Tab::Library => self.library.key(key, &mut fx),
                Tab::Downloads => self.downloads.key(key, &mut fx),
                Tab::Console => self.console.key(key, &self.catalog, &mut fx),
            },
        }
        self.apply(fx);
    }

    fn modal_key(&mut self, modal: Modal, key: KeyEvent) {
        let mut fx = Effects::default();
        let keep = match modal {
            Modal::Help => None,
            Modal::Confirm { text, command } => match key.code {
                KeyCode::Char('y') | KeyCode::Char('Y') | KeyCode::Enter => {
                    if let Command::Send(name, payload) = command {
                        fx.send(&name, payload);
                    }
                    None
                }
                KeyCode::Char('n') | KeyCode::Char('N') | KeyCode::Esc => None,
                _ => Some(Modal::Confirm { text, command }),
            },
            Modal::Filter(mut input) => match input.key(key) {
                InputAction::Submit(text) => {
                    self.library.set_filter(text.trim());
                    self.library.request_files(&mut fx);
                    None
                }
                InputAction::Cancel => {
                    self.library.set_filter("");
                    None
                }
                _ => {
                    self.library.set_filter(&input.text);
                    Some(Modal::Filter(input))
                }
            },
            Modal::AddDownload(mut input) => match input.key(key) {
                InputAction::Submit(url) => {
                    let url = url.trim();
                    if !url.is_empty() {
                        let torrent = url.starts_with("magnet:") || url.to_lowercase().split('?').next().is_some_and(|p| p.ends_with(".torrent"));
                        fx.send("DownloadAdd", json!({ "kind": if torrent { "Torrent" } else { "Direct" }, "url": url, "addToLibrary": true }));
                        fx.info("Adding download…");
                    }
                    None
                }
                InputAction::Cancel => None,
                _ => Some(Modal::AddDownload(input)),
            },
        };
        self.modal = keep;
        self.apply(fx);
    }

    fn tab_index(&self) -> usize {
        TABS.iter().position(|t| *t == self.tab).unwrap_or(0)
    }

    fn render(&mut self, f: &mut Frame) {
        let [top, body, footer] = Layout::vertical([Constraint::Length(1), Constraint::Min(0), Constraint::Length(1)]).areas(f.area());

        let (dot, dot_style) = match self.connected {
            Some(true) => ("●", Style::new().fg(Color::Green)),
            Some(false) => ("●", Style::new().fg(Color::Red)),
            None => ("○", Style::new().fg(Color::DarkGray)),
        };
        let right = Line::from(vec![Span::styled(dot, dot_style), Span::raw(format!(" {} ", self.server))]);
        let [tabs_area, server_area] = Layout::horizontal([Constraint::Min(0), Constraint::Length(right.width() as u16)]).areas(top);
        let downloading = self.downloads.list.iter().filter(|d| d.state == "Downloading").count();
        let titles = [
            "1 Library".to_string(),
            if downloading > 0 { format!("2 Downloads ({downloading}↓)") } else { "2 Downloads".into() },
            "3 Console".into(),
        ];
        let tabs = Tabs::new(titles)
            .select(self.tab_index())
            .highlight_style(Style::new().fg(Color::Yellow).add_modifier(Modifier::BOLD))
            .divider("│");
        f.render_widget(tabs, tabs_area);
        f.render_widget(right, server_area);

        match self.tab {
            Tab::Library => self.library.render(f, body),
            Tab::Downloads => self.downloads.render(f, body),
            Tab::Console => self.console.render(f, body),
        }

        let status = self.status.as_ref().filter(|(_, error, at)| at.elapsed() < Duration::from_secs(if *error { 8 } else { 4 }));
        let footer_line = match status {
            Some((text, true, _)) => Line::from(Span::raw(format!(" {text}")).red()),
            Some((text, false, _)) => Line::from(Span::raw(format!(" {text}")).green()),
            None => {
                let hints = match self.tab {
                    Tab::Library => self.library.hints(),
                    Tab::Downloads => self.downloads.hints(),
                    Tab::Console => self.console.hints(),
                };
                let global = if self.tab == Tab::Console && self.console.input_focus { "  F1-F3 tabs  Ctrl+C quit" } else { "  Tab/1-3 tabs  ? help  q quit" };
                Line::from(vec![Span::raw(format!(" {hints}")), Span::raw(global).dark_gray()])
            }
        };
        f.render_widget(footer_line, footer);

        if let Some(modal) = &self.modal {
            render_modal(f, modal);
        }
    }
}

fn centered(area: Rect, width: u16, height: u16) -> Rect {
    let [v] = Layout::vertical([Constraint::Length(height)]).flex(Flex::Center).areas(area);
    let [h] = Layout::horizontal([Constraint::Length(width.min(area.width))]).flex(Flex::Center).areas(v);
    h
}

fn render_modal(f: &mut Frame, modal: &Modal) {
    let area = f.area();
    match modal {
        Modal::Filter(input) | Modal::AddDownload(input) => {
            let (title, hint) = match modal {
                Modal::Filter(_) => (" Filter library ", " Enter keep · Esc clear "),
                _ => (" Add download: magnet link, .torrent URL or direct URL ", " Enter add to library · Esc cancel "),
            };
            let rect = centered(area, area.width.saturating_sub(8).min(100), 3);
            f.render_widget(Clear, rect);
            let block = Block::bordered().title(title).title_bottom(Line::from(hint).dark_gray()).border_style(Style::new().fg(Color::Yellow));
            // Show the end of long input (magnet links).
            let inner_width = rect.width.saturating_sub(2) as usize;
            let before: String = input.before_cursor().to_owned();
            let skip = before.chars().count().saturating_sub(inner_width.saturating_sub(1));
            let shown: String = input.text.chars().skip(skip).collect();
            f.render_widget(Paragraph::new(shown).block(block), rect);
            let x = rect.x + 1 + (before.chars().count() - skip) as u16;
            f.set_cursor_position(Position::new(x, rect.y + 1));
        }
        Modal::Confirm { text, .. } => {
            let rect = centered(area, (text.chars().count() as u16 + 6).clamp(30, area.width), 5);
            f.render_widget(Clear, rect);
            let body = vec![Line::raw(text.clone()), Line::raw(""), Line::from(vec!["y".bold().yellow(), " yes   ".into(), "n".bold().yellow(), " no".into()])];
            f.render_widget(
                Paragraph::new(body).wrap(Wrap { trim: false }).block(Block::bordered().title(" Confirm ").border_style(Style::new().fg(Color::Red))),
                rect,
            );
        }
        Modal::Help => {
            let lines: Vec<Line> = HELP
                .lines()
                .map(|l| match l.split_once("  ") {
                    Some((keys, what)) if !l.starts_with(' ') && !keys.is_empty() => {
                        Line::from(vec![Span::raw(format!("{keys:<18}")).yellow(), Span::raw(what.trim_start().to_owned())])
                    }
                    _ => Line::from(Span::raw(l.to_owned()).bold()),
                })
                .collect();
            let rect = centered(area, 72, lines.len() as u16 + 2);
            f.render_widget(Clear, rect);
            f.render_widget(Paragraph::new(lines).block(Block::bordered().title(" Keys (any key closes) ")), rect);
        }
    }
}

const HELP: &str = "\
Everywhere
Tab / 1-3  switch tab (F1-F3, Alt+1-3 also while typing)
r  refresh library and downloads
q / Ctrl+C  quit
Library
↑↓ j k PgUp PgDn  move
Enter  play the item (where you left off) or the selected file
→ / ←  between items and files
/  filter by title
R  rescan the item's folder
Downloads
Space / p  pause / resume
a  add a magnet link or URL (into the library)
x / X  remove (keep files) / remove and delete files
Console
Enter / i  type: <Event> [payload], listen <Topic> [key]
Tab  complete events and payload fields
↑↓  select message (history while typing)
PgUp/PgDn  scroll the payload
f  show / hide DownloadStats and DownloadList pushes
Esc  leave the input";
