# Popcorn.Cli (`popcorn-cli`)

Thin terminal client of a running `Popcorn.Server`, for scripting, testing and LLM agents. It speaks the web UI's
WebSocket RPC (`/ws`, `{ type, payload }` JSON, see [Docs/Architecture.md](../Docs/Architecture.md)) and prints every
message the server sends back as one JSON object per line. No logic of its own: whatever the UI can ask the server,
this can too.

```sh
cargo build --release -p popcorn-cli      # or: cargo xtask build cli; also in Bundle/Linux/popcorn-cli
```

## Commands

```sh
popcorn-cli                               # full-screen TUI (below)
popcorn-cli repl                          # line prompt (below)
popcorn-cli ping                          # is the server up? (exit 1 if not)
popcorn-cli events [--all]                # events the server handles + payload type (--all: also replies/pushes)
popcorn-cli describe DownloadAdd          # the payload's TypeScript type and every type it uses
popcorn-cli send <Event> [payload]        # send one event, print the replies
popcorn-cli listen <Topic> [key] [-n N] [--timeout S]   # Subscribe and print pushes (DownloadList, DownloadStats, SegmentsState <itemId>)
```

Payload (`send`): nothing = null; a JSON value; `-` = JSON from stdin; `key=value` (string) / `key:=json` pairs
build an object (httpie style); anything else is sent as a string. Events whose payload is a string (`FileOpen`,
`RefreshMediaItem`, ...) always get a string, so `FileOpen 123` sends `"123"`.

```sh
popcorn-cli send MediaList -p | jq '.recentItems[].title'
popcorn-cli send MediaItem id=<itemId>                  # MediaItem + MediaItemContinue + file lists
popcorn-cli send FileOpen <fileId>                      # play in the native player
popcorn-cli send DownloadAdd url='magnet:?xt=…' addToLibrary:=true --wait DownloadAdded
popcorn-cli send PluginFileSearch "$(jq -n '{query: "…"}')" --all --idle 10000
popcorn-cli send ImageFile <imagePath> --binary-dir /tmp/img   # binary replies saved to files
```

When `send` stops: at the first reply named like the event, or like a `--wait TYPE` (repeatable), plus 300 ms for
the replies the server sends along with it; else after `--idle` ms (default 1500) without a message. `--all` ignores
replies and only uses `--idle`; `--timeout` (default 30 s) caps everything. Many events reply under another name
(`DownloadAdd` → `DownloadAdded`, `RemoveLibraryFolder` → `LibraryFolders`) or not at all: use `--wait`, or let the
idle timeout end it.

Output: `{"type":..,"payload":..}` per line; binary frames `{"type":..,"params":[..],"bytes":n[,"file":..]}`.
`-p` prints payloads only, `--pretty` indents. Event names are case-insensitive; unknown ones are refused (the
server ignores them silently) unless `--force`.

Exit codes: 0 ok, 1 connection error or `ErrorWhenHandlingEvent` for the sent event (details in the server log),
2 usage error, 3 `--wait` type never came.

Server address: `--server http://host:port`, else `POPCORN_SERVER_URL`, else `http://localhost:$POPCORN_PORT`
(environment or `~/.config/popcorn/popcorn.env`, default 5000), the same lookup as `Popcorn.Linux`.

## TUI

`popcorn-cli` without a command, in a terminal, opens a full-screen UI (ratatui, `src/tui/`):

- **Library**: items (in the order of the home page, never-played ones marked `new`, resume point of the others) and
  the selected item's files in play order (`▸` = where you left off, `✓` played). Enter plays the item where you
  left off, or the selected file, in the native player via the server (`FileOpen`, so on the server's machine).
  `/` filters, `R` rescans the folder.
- **Downloads**: the `DownloadList` / `DownloadStats` subscriptions as a live table + the selected download's files.
  Space pauses/resumes, `a` adds a magnet link / `.torrent` URL / direct URL (into the library), `x` removes,
  `X` removes and deletes the files (both ask first).
- **Console**: every message in (`←`) and out (`→`), the selected one as coloured JSON, and an input line that takes
  what the repl does (`<Event> [payload]`, `listen`/`unlisten <Topic> [key]`, Tab completion, ↑↓ history).
  DownloadStats/DownloadList pushes are hidden unless `f`.

`?` lists the keys. Tabs: Tab / 1-3 (F1-F3 or Alt+1-3 also while typing in the console), `q` or Ctrl+C quits. The
connection runs in the background (`src/connection.rs`): when the server goes away the dot top right turns red, it
reconnects every 2 s and subscribes again.

## repl

`popcorn-cli repl` (also what no command does when stdin/stdout isn't a terminal) is a line prompt
(`popcorn> `): each line is `<Event> [payload]` (same payload syntax as `send`, shell-style quoting) or a built-in:
`listen`/`unlisten <Topic> [key]`, `events`, `describe <Event>`, `pretty`, `payload`, `help`, `exit`. Everything the
server sends is printed above the prompt as it arrives, including pushes such as `PluginList` (nothing waits for a
reply). Tab completes built-ins, event names, topics and the payload's field names (`RemoveMediaItem de<Tab>` →
`deleteFiles=`). History: `$XDG_STATE_HOME/popcorn/cli-history` (`~/.local/state/...`). Reconnects like the TUI and
queues the commands typed meanwhile. Piped input: `printf 'MediaList\n' | popcorn-cli repl` waits for the last
replies (up to 500 ms quiet) before leaving. Lines starting with `# ` are the CLI's own.

## Event list

`events` / `describe` read `Popcorn.Web/src/socket/socket-requests.ts` and `socket-payloads.ts`, embedded at compile
time (`src/catalog.rs`). Every server build regenerates them, so rebuild the CLI after adding a socket event. Replies
have no generated types (the web UI's hand-written `src/model`); look at the output or the handler.

Native player control is out of scope: the server → player link is one-way (`PlayerLink`), and the player's overlay
socket is a separate per-process bridge.
