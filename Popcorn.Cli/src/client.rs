//! Blocking WebSocket connection to the server's `/ws` (Docs/Architecture.md, "Web UI ↔ Server").

use std::io::ErrorKind;
use std::net::{TcpStream, ToSocketAddrs};
use std::time::{Duration, Instant};

use serde_json::{Value, json};
use tungstenite::{Message, WebSocket};

pub enum Incoming {
    /// `{ type, payload }` text frame.
    Event { kind: String, payload: Value },
    /// `"type,param1,param2;"` + bytes.
    Binary { kind: String, params: Vec<String>, data: Vec<u8> },
}

pub struct Client {
    ws: WebSocket<TcpStream>,
}

impl Client {
    /// `server` = `http://host:port` (the web UI's address).
    pub fn connect(server: &str) -> Result<Self, String> {
        let rest = server
            .strip_prefix("http://")
            .or_else(|| server.strip_prefix("ws://"))
            .ok_or_else(|| format!("unsupported server URL {server:?} (expected http://host:port)"))?;
        let authority = rest.split('/').next().unwrap_or(rest);
        let host_port = if authority.contains(':') && !authority.ends_with(']') {
            authority.to_owned()
        } else {
            format!("{authority}:80")
        };
        let addrs: Vec<_> = host_port
            .to_socket_addrs()
            .map_err(|e| format!("cannot resolve {host_port}: {e}"))?
            .collect();
        let mut last_err = None;
        let stream = addrs
            .iter()
            .find_map(|a| match TcpStream::connect_timeout(a, Duration::from_secs(3)) {
                Ok(s) => Some(s),
                Err(e) => {
                    last_err = Some(e);
                    None
                }
            })
            .ok_or_else(|| {
                format!(
                    "Popcorn.Server not reachable at {server}: {}",
                    last_err.map(|e| e.to_string()).unwrap_or_else(|| "no address".into())
                )
            })?;
        stream.set_nodelay(true).ok();
        let url = format!("ws://{authority}/ws");
        let (ws, _) = tungstenite::client(url.as_str(), stream).map_err(|e| format!("WebSocket handshake with {url} failed: {e}"))?;
        Ok(Self { ws })
    }

    pub fn send(&mut self, kind: &str, payload: &Value) -> Result<(), String> {
        let text = json!({ "type": kind, "payload": payload }).to_string();
        self.ws.send(Message::text(text)).map_err(|e| format!("send failed: {e}"))
    }

    /// Next message, or None when nothing arrived before `deadline`.
    pub fn recv(&mut self, deadline: Instant) -> Result<Option<Incoming>, String> {
        loop {
            let left = deadline.saturating_duration_since(Instant::now());
            if left.is_zero() {
                return Ok(None);
            }
            self.ws.get_ref().set_read_timeout(Some(left)).ok();
            match self.ws.read() {
                Ok(Message::Text(text)) => {
                    let v: Value = serde_json::from_str(&text).map_err(|e| format!("bad message from server: {e}"))?;
                    let kind = v.get("type").and_then(Value::as_str).unwrap_or_default().to_owned();
                    let payload = v.get("payload").cloned().unwrap_or(Value::Null);
                    return Ok(Some(Incoming::Event { kind, payload }));
                }
                Ok(Message::Binary(bytes)) => {
                    let split = bytes.iter().position(|b| *b == b';').unwrap_or(bytes.len());
                    let header = String::from_utf8_lossy(&bytes[..split]);
                    let mut parts = header.split(',').map(str::to_owned);
                    let kind = parts.next().unwrap_or_default();
                    let params = parts.filter(|p| !p.is_empty()).collect();
                    let data = bytes.get(split + 1..).unwrap_or_default().to_vec();
                    return Ok(Some(Incoming::Binary { kind, params, data }));
                }
                Ok(Message::Close(_)) => return Err("server closed the connection".into()),
                Ok(_) => continue,
                Err(tungstenite::Error::Io(e)) if matches!(e.kind(), ErrorKind::WouldBlock | ErrorKind::TimedOut) => {
                    return Ok(None);
                }
                Err(e) => return Err(format!("connection lost: {e}")),
            }
        }
    }

    pub fn close(mut self) {
        let _ = self.ws.close(None);
        // Let the close handshake finish so the server doesn't log an aborted socket.
        let deadline = Instant::now() + Duration::from_millis(500);
        while self.recv(deadline).is_ok_and(|m| m.is_some()) {}
    }
}
