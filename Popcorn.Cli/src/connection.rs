//! Background connection for the long-running modes (TUI, repl): owns the socket, sends what the front end queues,
//! hands over whatever arrives, reconnects every 2 s when the server goes away and subscribes to the topics again.

use std::sync::mpsc::{Receiver, RecvTimeoutError, TryRecvError};
use std::thread::{self, JoinHandle};
use std::time::{Duration, Instant};

use serde_json::Value;

use crate::client::{Client, Incoming};

pub enum Command {
    Send(String, Value),
    /// `{ topic, key }`; kept and sent again after a reconnect.
    Subscribe(Value),
    Unsubscribe(Value),
    /// Close now, without waiting for replies still on their way (the TUI's quit).
    Close,
}

pub enum Event {
    /// Every (re)connect.
    Connected,
    /// Once per outage, with the reason.
    Down(String),
    Message(Incoming),
}

/// Runs until the sender of `commands` is dropped; then prints nothing more than what arrives within 500 ms of quiet
/// (the replies still on their way, e.g. at the end of piped input).
pub fn spawn(server: String, commands: Receiver<Command>, mut emit: impl FnMut(Event) + Send + 'static) -> JoinHandle<()> {
    thread::spawn(move || {
        let mut subscriptions: Vec<Value> = Vec::new();
        let mut pending: Vec<Command> = Vec::new();
        let mut down = false;
        loop {
            let mut client = match Client::connect(&server) {
                Ok(c) => c,
                Err(e) => {
                    if !down {
                        emit(Event::Down(e));
                        down = true;
                    }
                    match commands.recv_timeout(Duration::from_secs(2)) {
                        Ok(Command::Close) => return,
                        Ok(cmd) => pending.push(cmd),
                        Err(RecvTimeoutError::Timeout) => {}
                        Err(RecvTimeoutError::Disconnected) => return,
                    }
                    continue;
                }
            };
            emit(Event::Connected);
            let mut ok = subscriptions.iter().all(|s| client.send("Subscribe", s).is_ok());
            for cmd in pending.drain(..) {
                ok = ok && run(&mut client, cmd, &mut subscriptions).is_ok();
            }

            if !ok {
                emit(Event::Down("connection lost".into()));
                down = true;
                continue;
            }
            'connected: loop {
                loop {
                    match commands.try_recv() {
                        Ok(Command::Close) => {
                            client.close();
                            return;
                        }
                        Ok(cmd) => {
                            if let Err(e) = run(&mut client, cmd, &mut subscriptions) {
                                emit(Event::Down(e));
                                break 'connected;
                            }
                        }
                        Err(TryRecvError::Empty) => break,
                        Err(TryRecvError::Disconnected) => {
                            let end = Instant::now() + Duration::from_secs(5);
                            while let Ok(Some(m)) = client.recv((Instant::now() + Duration::from_millis(500)).min(end)) {
                                emit(Event::Message(m));
                            }
                            client.close();
                            return;
                        }
                    }
                }
                match client.recv(Instant::now() + Duration::from_millis(30)) {
                    Ok(Some(message)) => emit(Event::Message(message)),
                    Ok(None) => {}
                    Err(e) => {
                        emit(Event::Down(e));
                        break;
                    }
                }
            }
            down = true;
        }
    })
}

fn run(client: &mut Client, cmd: Command, subscriptions: &mut Vec<Value>) -> Result<(), String> {
    match cmd {
        Command::Send(name, payload) => client.send(&name, &payload),
        Command::Subscribe(request) => {
            if !subscriptions.contains(&request) {
                subscriptions.push(request.clone());
            }
            client.send("Subscribe", &request)
        }
        Command::Unsubscribe(request) => {
            subscriptions.retain(|s| *s != request);
            client.send("Unsubscribe", &request)
        }
        Command::Close => Ok(()),
    }
}
