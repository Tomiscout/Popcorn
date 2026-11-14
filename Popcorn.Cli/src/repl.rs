//! `popcorn-cli repl`: a line prompt that sends events, with everything the server sends printed above it as it
//! arrives (replies and subscription pushes alike). Works over pipes too.

use std::process::ExitCode;
use std::sync::Arc;
use std::sync::atomic::Ordering;
use std::sync::mpsc;

use rustyline::completion::{Completer, Pair};
use rustyline::error::ReadlineError;
use rustyline::highlight::Highlighter;
use rustyline::hint::Hinter;
use rustyline::history::DefaultHistory;
use rustyline::validate::Validator;
use rustyline::{Context, Editor, ExternalPrinter, Helper};

use crate::Output;
use crate::catalog::Catalog;
use crate::connection::{self, Command, Event};
use crate::line;

const HELP: &str = "\
<Event> [payload]       send an event, e.g. `MediaList`, `MediaItem id=<id>`, `FileOpen <fileId>`
                        payload: JSON, key=value / key:=json pairs, or text (see `popcorn-cli send --help`)
                        --force sends an event that isn't in the contract
listen <Topic> [key]    subscribe (DownloadList, DownloadStats, SegmentsState <itemId>); kept across reconnects
unlisten <Topic> [key]  unsubscribe
events [--all]          events the server handles (--all: also the ones it only sends)
describe <Event>        payload type of an event
pretty [on|off]         indent JSON
payload [on|off]        print payloads only
help                    this text
exit, quit, Ctrl+D      leave (Tab completes events and payload fields; Ctrl+C clears the line)";

const BUILTINS: [&str; 9] = ["listen", "unlisten", "events", "describe", "pretty", "payload", "help", "exit", "quit"];

pub fn run(catalog: Catalog, out: Output, server: String) -> ExitCode {
    let catalog = Arc::new(catalog);
    let out = Arc::new(out);
    let mut editor: Editor<ReplHelper, DefaultHistory> = match Editor::new() {
        Ok(e) => e,
        Err(e) => {
            eprintln!("popcorn-cli: cannot start the prompt: {e}");
            return ExitCode::from(1);
        }
    };
    editor.set_helper(Some(ReplHelper { catalog: catalog.clone() }));
    let history = history_file();
    if let Some(path) = &history {
        let _ = editor.load_history(path);
    }

    // Messages are printed above the prompt; without a terminal (piped stdin) straight to stdout.
    let mut print: Box<dyn FnMut(String) + Send> = match editor.create_external_printer() {
        Ok(mut p) => Box::new(move |s: String| {
            let _ = p.print(s);
        }),
        Err(_) => Box::new(|s: String| println!("{s}")),
    };

    let (tx, rx) = mpsc::channel::<Command>();
    let socket = {
        let out = out.clone();
        let mut was_down = false;
        connection::spawn(server.clone(), rx, move |event| match event {
            Event::Connected if was_down => {
                was_down = false;
                print("# connected".into());
            }
            Event::Connected => {}
            Event::Down(e) => {
                was_down = true;
                print(format!("# {e}; retrying every 2 s (commands are queued)"));
            }
            Event::Message(m) => print(out.format(&m, None)),
        })
    };

    println!("# popcorn-cli {} → {server}. `help` lists commands.", env!("CARGO_PKG_VERSION"));
    loop {
        let input = match editor.readline("popcorn> ") {
            Ok(l) => l,
            Err(ReadlineError::Interrupted) => continue,
            Err(ReadlineError::Eof) => break,
            Err(e) => {
                eprintln!("popcorn-cli: {e}");
                break;
            }
        };
        let input = input.trim();
        if input.is_empty() {
            continue;
        }
        let _ = editor.add_history_entry(input);
        let words = match line::split_words(input) {
            Ok(w) => w,
            Err(e) => {
                println!("# {e}");
                continue;
            }
        };
        let (head, rest) = words.split_first().expect("non-empty line");
        let command = match head.to_ascii_lowercase().as_str() {
            "exit" | "quit" => break,
            "help" | "?" => {
                println!("{HELP}");
                continue;
            }
            "events" => {
                crate::events(&catalog, rest.iter().any(|a| a == "--all"));
                continue;
            }
            "describe" => {
                match rest {
                    [event] => {
                        if crate::describe(&catalog, event).is_err() {
                            println!("# unknown event {event:?}");
                        }
                    }
                    _ => println!("# usage: describe <Event>"),
                }
                continue;
            }
            "pretty" | "payload" => {
                let flag = if head.eq_ignore_ascii_case("pretty") { &out.pretty } else { &out.payload_only };
                let value = match rest.first().map(String::as_str) {
                    Some("on") => true,
                    Some("off") => false,
                    _ => !flag.load(Ordering::Relaxed),
                };
                flag.store(value, Ordering::Relaxed);
                println!("# {} {}", head.to_ascii_lowercase(), if value { "on" } else { "off" });
                continue;
            }
            "listen" | "unlisten" => match line::subscription(rest) {
                Some(request) if head.eq_ignore_ascii_case("listen") => Command::Subscribe(request),
                Some(request) => Command::Unsubscribe(request),
                None => {
                    println!("# usage: {} <Topic> [key]", head.to_ascii_lowercase());
                    continue;
                }
            },
            _ => {
                let force = rest.iter().any(|a| a == "--force");
                let payload: Vec<String> = rest.iter().filter(|a| *a != "--force").cloned().collect();
                match crate::request(&catalog, head, &payload, force) {
                    Ok((name, payload)) => Command::Send(name, payload),
                    Err(e) => {
                        println!("# {e}");
                        continue;
                    }
                }
            }
        };
        if tx.send(command).is_err() {
            break;
        }
    }

    if let Some(path) = &history {
        if let Some(dir) = path.parent() {
            let _ = std::fs::create_dir_all(dir);
        }
        let _ = editor.save_history(path);
    }
    drop(tx);
    let _ = socket.join();
    ExitCode::SUCCESS
}

fn history_file() -> Option<std::path::PathBuf> {
    let state = std::env::var_os("XDG_STATE_HOME")
        .map(std::path::PathBuf::from)
        .filter(|p| p.is_absolute())
        .or_else(|| std::env::var_os("HOME").map(|h| std::path::PathBuf::from(h).join(".local/state")))
        .or_else(|| std::env::var_os("LOCALAPPDATA").map(std::path::PathBuf::from))?;
    Some(state.join("popcorn/cli-history"))
}

struct ReplHelper {
    catalog: Arc<Catalog>,
}

impl Completer for ReplHelper {
    type Candidate = Pair;

    fn complete(&self, line: &str, pos: usize, _: &Context<'_>) -> rustyline::Result<(usize, Vec<Pair>)> {
        let (start, candidates) = line::complete(&self.catalog, &BUILTINS, &line[..pos]);
        Ok((start, candidates.into_iter().map(|c| Pair { display: c.display, replacement: c.replacement }).collect()))
    }
}

impl Hinter for ReplHelper {
    type Hint = String;
}
impl Highlighter for ReplHelper {}
impl Validator for ReplHelper {}
impl Helper for ReplHelper {}
