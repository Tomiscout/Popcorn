//! `popcorn-cli`: thin terminal client of a running Popcorn.Server. Speaks the web UI's WebSocket RPC (`/ws`,
//! `{ type, payload }` JSON) and prints what the server sends back as JSON lines, so scripts and LLM agents can
//! drive the server without the UI. README.md has the details.

mod catalog;
mod client;
mod connection;
mod line;
mod repl;
mod tui;

use std::io::{IsTerminal, Read, Write};
use std::path::PathBuf;
use std::process::ExitCode;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::{Duration, Instant};

use clap::{Parser, Subcommand};
use serde_json::{Map, Value, json};

use catalog::Catalog;
use client::{Client, Incoming};

/// `println!` that ends the program quietly when stdout is closed (`| head`) instead of panicking.
macro_rules! line {
    ($($arg:tt)*) => {{
        let mut stdout = std::io::stdout().lock();
        if writeln!(stdout, $($arg)*).is_err() {
            std::process::exit(0);
        }
    }};
}

const DEFAULT_PORT: u16 = 5000;
const ERROR_EVENT: &str = "ErrorWhenHandlingEvent";

#[derive(Parser)]
#[command(
    name = "popcorn-cli",
    version,
    about = "Control a running Popcorn.Server from the terminal",
    after_help = "Server: --server, else POPCORN_SERVER_URL, else http://localhost:$POPCORN_PORT \
                  (env or ~/.config/popcorn/popcorn.env, default 5000).\n\
                  Output: one JSON object per line: {\"type\":..,\"payload\":..} (binary frames: \
                  {\"type\":..,\"params\":[..],\"bytes\":n}).\n\
                  Exit codes: 0 ok, 1 server/connection error, 2 usage error, 3 --wait timed out."
)]
struct Cli {
    /// Server address, e.g. http://localhost:5000
    #[arg(long, global = true)]
    server: Option<String>,
    /// Pretty-print JSON (multi-line) instead of one object per line
    #[arg(long, global = true)]
    pretty: bool,
    /// Print only the payloads
    #[arg(short = 'p', long = "payload-only", global = true)]
    payload_only: bool,
    /// No command: full-screen UI in a terminal (Library, Downloads, Console), else (piped input) `repl`
    #[command(subcommand)]
    command: Option<Command>,
}

#[derive(Subcommand)]
enum Command {
    /// Send one event and print the server's replies
    #[command(after_help = "PAYLOAD:\n  \
        (none)            null (events that take no payload)\n  \
        '{\"id\":\"..\"}'      JSON value\n  \
        -                 JSON read from stdin\n  \
        key=value ...     object with string fields; key:=json for numbers/bools/objects (httpie style)\n  \
        text              a JSON string (also for events whose payload is a string, e.g. FileOpen <fileId>)\n\n\
        Stops at the first reply named like the event (or a --wait type) plus a short grace period for the replies \
        sent along with it, else after --idle ms without messages.\n\n\
        Examples:\n  \
        popcorn-cli send MediaList -p\n  \
        popcorn-cli send MediaItem id=<itemId>\n  \
        popcorn-cli send FileOpen <fileId>\n  \
        popcorn-cli send DownloadAdd url='magnet:?xt=...' addToLibrary:=true --wait DownloadAdded\n  \
        popcorn-cli send PluginFileSearch '{\"query\":\"...\"}' --all --idle 10000")]
    Send {
        /// Event name (`popcorn-cli events`); case-insensitive
        event: String,
        /// Payload, see below
        payload: Vec<String>,
        /// Stop once a message of this type arrives (repeatable); default: the event's own name
        #[arg(short, long = "wait", value_name = "TYPE")]
        wait: Vec<String>,
        /// Don't stop on a reply: print everything until --idle passes without messages
        #[arg(long, conflicts_with = "wait")]
        all: bool,
        /// Milliseconds without any message after which to stop
        #[arg(long, default_value_t = 1500, value_name = "MS")]
        idle: u64,
        /// Give up after this many seconds in any case
        #[arg(long, default_value_t = 30, value_name = "SECS")]
        timeout: u64,
        /// Save binary frames (images, thumbnails) into this folder
        #[arg(long, value_name = "DIR")]
        binary_dir: Option<PathBuf>,
        /// Send even when the event isn't in the embedded contract (older/newer server)
        #[arg(long)]
        force: bool,
    },
    /// Subscribe to a pushed topic (DownloadList, DownloadStats, SegmentsState <itemId>) and print updates
    Listen {
        topic: String,
        key: Option<String>,
        /// Stop after this many messages
        #[arg(short = 'n', long)]
        count: Option<usize>,
        /// Stop after this many seconds
        #[arg(long, value_name = "SECS")]
        timeout: Option<u64>,
    },
    /// List the events the server handles and their payload types
    Events {
        /// Also list events the server only sends (replies/pushes)
        #[arg(long)]
        all: bool,
    },
    /// Show an event's payload type (TypeScript, as generated from the server)
    Describe { event: String },
    /// Check that the server answers on /ws
    Ping,
    /// Line prompt: type events, see everything the server sends (also over pipes; `help` inside)
    Repl,
}

fn main() -> ExitCode {
    let cli = Cli::parse();
    let out = Output::new(cli.pretty, cli.payload_only);
    let catalog = Catalog::load();
    let command = match cli.command {
        Some(Command::Repl) => return repl::run(catalog, out, server_url(cli.server)),
        Some(c) => c,
        None if std::io::stdin().is_terminal() && std::io::stdout().is_terminal() => {
            return tui::run(catalog, server_url(cli.server));
        }
        None => return repl::run(catalog, out, server_url(cli.server)),
    };
    let result = match command {
        Command::Events { all } => {
            events(&catalog, all);
            Ok(ExitCode::SUCCESS)
        }
        Command::Describe { event } => describe(&catalog, &event),
        Command::Ping => {
            let server = server_url(cli.server);
            Client::connect(&server).map(|c| {
                c.close();
                line!("ok {server}");
                ExitCode::SUCCESS
            })
            .map_err(Failure::Other)
        }
        Command::Send { event, payload, wait, all, idle, timeout, binary_dir, force } => {
            let server = server_url(cli.server);
            send(&catalog, &out, &server, SendArgs { event, payload, wait, all, idle, timeout, binary_dir, force })
        }
        Command::Listen { topic, key, count, timeout } => {
            let server = server_url(cli.server);
            listen(&out, &server, &topic, key, count, timeout)
        }
        Command::Repl => unreachable!("handled above"),
    };
    match result {
        Ok(code) => code,
        Err(Failure::Usage(msg)) => {
            eprintln!("popcorn-cli: {msg}");
            ExitCode::from(2)
        }
        Err(Failure::Other(msg)) => {
            eprintln!("popcorn-cli: {msg}");
            ExitCode::from(1)
        }
    }
}

enum Failure {
    Usage(String),
    Other(String),
}

impl From<String> for Failure {
    fn from(s: String) -> Self {
        Failure::Other(s)
    }
}

struct SendArgs {
    event: String,
    payload: Vec<String>,
    wait: Vec<String>,
    all: bool,
    idle: u64,
    timeout: u64,
    binary_dir: Option<PathBuf>,
    force: bool,
}

/// Replies sent together with the awaited one (MediaItem → MediaItemContinue, MediaItemFlatFileList, ...).
const GRACE: Duration = Duration::from_millis(300);

fn send(catalog: &Catalog, out: &Output, server: &str, args: SendArgs) -> Result<ExitCode, Failure> {
    let (name, payload) = request(catalog, &args.event, &args.payload, args.force).map_err(Failure::Usage)?;

    let explicit_wait = !args.wait.is_empty();
    let wait: Vec<String> = if args.all {
        Vec::new()
    } else if explicit_wait {
        args.wait.iter().map(|w| catalog.event(w).map_or(w.clone(), |e| e.name.clone())).collect()
    } else {
        vec![name.clone()]
    };

    let mut client = Client::connect(server)?;
    client.send(&name, &payload)?;

    let hard_deadline = Instant::now() + Duration::from_secs(args.timeout);
    let idle = Duration::from_millis(args.idle);
    let mut matched = false;
    let mut binary_count = 0usize;
    let code = loop {
        // With an explicit --wait, quiet periods don't end the wait; after the match only the grace period does.
        let deadline = if matched {
            Instant::now() + GRACE
        } else if explicit_wait {
            hard_deadline
        } else {
            Instant::now() + idle
        }
        .min(hard_deadline);
        match client.recv(deadline)? {
            None => {
                if explicit_wait && !matched {
                    eprintln!("popcorn-cli: timed out waiting for {}", wait.join(" / "));
                    break ExitCode::from(3);
                }
                break ExitCode::SUCCESS;
            }
            Some(message @ Incoming::Event { .. }) => {
                out.print(&message, None);
                let Incoming::Event { kind, payload } = message else { unreachable!() };
                if kind == ERROR_EVENT && payload.as_str() == Some(name.as_str()) {
                    eprintln!("popcorn-cli: the server failed to handle {name} (details in the server log)");
                    break ExitCode::from(1);
                }
                if wait.contains(&kind) {
                    matched = true;
                }
            }
            Some(message @ Incoming::Binary { .. }) => {
                let Incoming::Binary { kind, params, data } = &message else { unreachable!() };
                let saved = match &args.binary_dir {
                    Some(dir) => Some(save_binary(dir, kind, params, data, binary_count)?),
                    None => None,
                };
                binary_count += 1;
                matched |= wait.contains(kind);
                out.print(&message, saved.as_deref());
            }
        }
    };
    client.close();
    Ok(code)
}

fn listen(out: &Output, server: &str, topic: &str, key: Option<String>, count: Option<usize>, timeout: Option<u64>) -> Result<ExitCode, Failure> {
    let mut client = Client::connect(server)?;
    let request = json!({ "topic": topic, "key": key });
    client.send("Subscribe", &request)?;
    let end = timeout.map(|s| Instant::now() + Duration::from_secs(s));
    let mut seen = 0usize;
    loop {
        if count.is_some_and(|n| seen >= n) {
            break;
        }
        let deadline = end.unwrap_or_else(|| Instant::now() + Duration::from_secs(3600));
        match client.recv(deadline)? {
            None if end.is_some() => break,
            None => continue,
            Some(message) => {
                out.print(&message, None);
                seen += 1;
            }
        }
    }
    let _ = client.send("Unsubscribe", &request);
    client.close();
    Ok(ExitCode::SUCCESS)
}

pub(crate) fn events(catalog: &Catalog, all: bool) {
    let width = catalog.events.iter().map(|e| e.name.len()).max().unwrap_or(0);
    for e in &catalog.events {
        let ty = match &e.payload {
            Some(t) => t.as_str(),
            None if all => "(server → client only)",
            None => continue,
        };
        let doc = e.doc.as_deref().map(|d| format!("  // {d}")).unwrap_or_default();
        line!("{:width$}  {ty}{doc}", e.name);
    }
}

pub(crate) fn describe(catalog: &Catalog, event: &str) -> Result<ExitCode, Failure> {
    let e = catalog
        .event(event)
        .ok_or_else(|| Failure::Usage(format!("unknown event {event:?}: see `popcorn-cli events --all`")))?;
    if let Some(doc) = &e.doc {
        line!("// {doc}");
    }
    match &e.payload {
        None => line!("{}: sent by the server only (a reply or push), no handler", e.name),
        Some(ty) => {
            line!("{}: payload {ty}\n", e.name);
            for decl in catalog.type_closure(ty) {
                line!("{}", decl.text);
            }
        }
    }
    Ok(ExitCode::SUCCESS)
}

/// Event name (canonical spelling) and payload JSON of a `send`; refuses events without a server handler unless
/// `force`.
pub(crate) fn request(catalog: &Catalog, event: &str, payload: &[String], force: bool) -> Result<(String, Value), String> {
    let known = catalog.event(event);
    let (name, payload_type) = match known {
        Some(e) if e.payload.is_some() => (e.name.clone(), e.payload.clone()),
        Some(e) if !force => {
            return Err(format!("{} is only sent by the server, it has no handler (--force sends it anyway)", e.name));
        }
        None if !force => {
            return Err(format!("unknown event {event:?}: see `events` (--force sends it anyway)"));
        }
        _ => (known.map_or(event.to_owned(), |e| e.name.clone()), None),
    };
    Ok((name, build_payload(payload, payload_type.as_deref())?))
}

/// Payload arguments → JSON (see `send --help`). `payload_type` = the handler's TypeScript type, when known.
fn build_payload(args: &[String], payload_type: Option<&str>) -> Result<Value, String> {
    let is_field = |a: &str| {
        let key_end = a.find(['=', ':']).unwrap_or(0);
        key_end > 0
            && a[..key_end].chars().all(|c| c.is_ascii_alphanumeric() || c == '_')
            && a[..key_end].chars().next().is_some_and(|c| c.is_ascii_alphabetic() || c == '_')
            && (a[key_end..].starts_with('=') || a[key_end..].starts_with(":="))
    };
    match args {
        [] => Ok(Value::Null),
        [one] if one == "-" => {
            let mut text = String::new();
            std::io::stdin().read_to_string(&mut text).map_err(|e| format!("cannot read stdin: {e}"))?;
            serde_json::from_str(&text).map_err(|e| format!("stdin is not JSON: {e}"))
        }
        _ if payload_type == Some("string") => match args {
            [one] => Ok(Value::String(
                serde_json::from_str::<Value>(one).ok().and_then(|v| v.as_str().map(str::to_owned)).unwrap_or_else(|| one.clone()),
            )),
            _ => Err("this event takes a single string".into()),
        },
        _ if payload_type == Some("string[]") && !args.iter().any(|a| a.starts_with('[')) => {
            Ok(Value::Array(args.iter().cloned().map(Value::String).collect()))
        }
        _ if args.iter().all(|a| is_field(a)) => {
            let mut map = Map::new();
            for a in args {
                if let Some((k, v)) = a.split_once(":=").filter(|(k, _)| !k.contains('=')) {
                    let v = serde_json::from_str(v).map_err(|e| format!("{k}:= is not JSON ({e}): {v}"))?;
                    map.insert(k.to_owned(), v);
                } else if let Some((k, v)) = a.split_once('=') {
                    map.insert(k.to_owned(), Value::String(v.to_owned()));
                }
            }
            Ok(Value::Object(map))
        }
        [one] => Ok(serde_json::from_str(one).unwrap_or_else(|_| Value::String(one.clone()))),
        _ => Err("several payload arguments must all be key=value / key:=json".into()),
    }
}

fn save_binary(dir: &std::path::Path, kind: &str, params: &[String], data: &[u8], index: usize) -> Result<String, String> {
    std::fs::create_dir_all(dir).map_err(|e| format!("cannot create {}: {e}", dir.display()))?;
    let ext = match data {
        [0x89, b'P', b'N', b'G', ..] => "png",
        [0xFF, 0xD8, ..] => "jpg",
        [b'R', b'I', b'F', b'F', _, _, _, _, b'W', b'E', b'B', b'P', ..] => "webp",
        [b'G', b'I', b'F', ..] => "gif",
        _ => "bin",
    };
    let stem: String = std::iter::once(kind)
        .chain(params.iter().map(String::as_str))
        .collect::<Vec<_>>()
        .join("-")
        .chars()
        .map(|c| if c.is_ascii_alphanumeric() || c == '-' || c == '.' { c } else { '_' })
        .take(120)
        .collect();
    let path = dir.join(format!("{index:03}-{stem}.{ext}"));
    std::fs::write(&path, data).map_err(|e| format!("cannot write {}: {e}", path.display()))?;
    Ok(path.display().to_string())
}

/// How messages are printed; switchable at run time (interactive `pretty` / `payload`).
pub(crate) struct Output {
    pub pretty: AtomicBool,
    pub payload_only: AtomicBool,
}

impl Output {
    fn new(pretty: bool, payload_only: bool) -> Self {
        Self { pretty: AtomicBool::new(pretty), payload_only: AtomicBool::new(payload_only) }
    }

    pub fn format(&self, message: &Incoming, saved: Option<&str>) -> String {
        let v = match message {
            Incoming::Event { kind, payload } if !self.payload_only.load(Ordering::Relaxed) => {
                json!({ "type": kind, "payload": payload })
            }
            Incoming::Event { payload, .. } => payload.clone(),
            Incoming::Binary { kind, params, data } => {
                let mut v = json!({ "type": kind, "params": params, "bytes": data.len() });
                if let Some(path) = saved {
                    v["file"] = Value::String(path.to_owned());
                }
                v
            }
        };
        if self.pretty.load(Ordering::Relaxed) { serde_json::to_string_pretty(&v) } else { serde_json::to_string(&v) }
            .unwrap_or_default()
    }

    fn print(&self, message: &Incoming, saved: Option<&str>) {
        let text = self.format(message, saved);
        let mut stdout = std::io::stdout().lock();
        // A closed pipe (`| head`) just ends the output.
        if writeln!(stdout, "{text}").and_then(|_| stdout.flush()).is_err() {
            std::process::exit(0);
        }
    }
}

/// Same lookup as Popcorn.Linux (`server.rs`): POPCORN_SERVER_URL, else localhost:POPCORN_PORT from the
/// environment or the service's env file.
fn server_url(explicit: Option<String>) -> String {
    if let Some(url) = explicit.or_else(|| std::env::var("POPCORN_SERVER_URL").ok()) {
        let url = url.trim().trim_end_matches('/');
        if !url.is_empty() {
            return url.to_owned();
        }
    }
    let port = std::env::var("POPCORN_PORT")
        .ok()
        .or_else(service_env_port)
        .and_then(|p| p.trim().parse::<u16>().ok())
        .filter(|p| *p > 0)
        .unwrap_or(DEFAULT_PORT);
    format!("http://localhost:{port}")
}

/// POPCORN_PORT of `~/.config/popcorn/popcorn.env` (the systemd unit's EnvironmentFile).
fn service_env_port() -> Option<String> {
    let config = std::env::var_os("XDG_CONFIG_HOME")
        .map(PathBuf::from)
        .filter(|p| p.is_absolute())
        .or_else(|| std::env::var_os("HOME").map(|h| PathBuf::from(h).join(".config")))?;
    let text = std::fs::read_to_string(config.join("popcorn/popcorn.env")).ok()?;
    text.lines().map(str::trim).filter(|l| !l.starts_with('#')).find_map(|l| {
        let (k, v) = l.split_once('=')?;
        (k.trim() == "POPCORN_PORT").then(|| v.trim().trim_matches(['"', '\'']).to_owned())
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn args(a: &[&str]) -> Vec<String> {
        a.iter().map(|s| s.to_string()).collect()
    }

    #[test]
    fn payloads() {
        assert_eq!(build_payload(&[], None).unwrap(), Value::Null);
        assert_eq!(build_payload(&args(&["id=abc", "n:=3", "b:=true"]), None).unwrap(), json!({"id":"abc","n":3,"b":true}));
        assert_eq!(build_payload(&args(&["123"]), Some("string")).unwrap(), json!("123"));
        assert_eq!(build_payload(&args(&["\"x\""]), Some("string")).unwrap(), json!("x"));
        assert_eq!(build_payload(&args(&["{\"a\":1}"]), Some("Foo")).unwrap(), json!({"a":1}));
        assert_eq!(build_payload(&args(&["magnet:?xt=urn:btih:x"]), None).unwrap(), json!("magnet:?xt=urn:btih:x"));
        assert_eq!(build_payload(&args(&["a", "b"]), Some("string[]")).unwrap(), json!(["a", "b"]));
        assert_eq!(build_payload(&args(&["url=magnet:?xt=1&dn=a=b"]), None).unwrap(), json!({"url":"magnet:?xt=1&dn=a=b"}));
        assert!(build_payload(&args(&["a", "b"]), None).is_err());
    }
}
