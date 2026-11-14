//! The server's socket contract, read from the TypeScript files every server build generates
//! (`Popcorn.Server/Contracts/SocketContractWriter.cs`). Embedded at compile time, so rebuilding the CLI after a
//! server build picks up new events.

const REQUESTS_TS: &str = include_str!("../../Popcorn.Web/src/socket/socket-requests.ts");
const PAYLOADS_TS: &str = include_str!("../../Popcorn.Web/src/socket/socket-payloads.ts");

pub struct Event {
    pub name: String,
    pub doc: Option<String>,
    /// TypeScript type of the payload the server's handler takes; None = only sent by the server.
    pub payload: Option<String>,
}

/// One top-level `export interface` / `export type` of socket-payloads.ts, with its doc comment.
pub struct TypeDecl {
    pub name: String,
    pub text: String,
}

pub struct Catalog {
    pub events: Vec<Event>,
    pub types: Vec<TypeDecl>,
}

impl Catalog {
    pub fn load() -> Self {
        let types = parse_types(PAYLOADS_TS);
        let payloads = types
            .iter()
            .find(|t| t.name == "SocketPayloads")
            .map(|t| parse_payload_map(&t.text))
            .unwrap_or_default();
        let events = parse_requests(REQUESTS_TS)
            .into_iter()
            .map(|(name, doc)| {
                let payload = payloads.iter().find(|(n, _)| *n == name).map(|(_, t)| t.clone());
                Event { name, doc, payload }
            })
            .collect();
        let types = types.into_iter().filter(|t| t.name != "SocketPayloads").collect();
        Self { events, types }
    }

    /// Exact name, else a case-insensitive match (`medialist` → `MediaList`).
    pub fn event(&self, name: &str) -> Option<&Event> {
        self.events
            .iter()
            .find(|e| e.name == name)
            .or_else(|| self.events.iter().find(|e| e.name.eq_ignore_ascii_case(name)))
    }

    /// Field names of an interface (`MediaItemRequest` → id), for completion.
    pub fn fields(&self, type_name: &str) -> Vec<String> {
        let Some(decl) = self.types.iter().find(|t| t.name == type_name) else { return Vec::new() };
        let Some((_, body)) = decl.text.split_once('{') else { return Vec::new() };
        body.lines()
            .map(str::trim)
            .filter(|l| !l.starts_with("/**") && !l.starts_with('*'))
            .filter_map(|l| {
                let name: String = l.chars().take_while(|c| c.is_ascii_alphanumeric() || *c == '_').collect();
                (!name.is_empty() && l[name.len()..].trim_start().starts_with(['?', ':'])).then_some(name)
            })
            .collect()
    }

    /// The declaration of `type_name` and every declared type it uses, depth first, each once.
    pub fn type_closure(&self, type_expr: &str) -> Vec<&TypeDecl> {
        let mut out: Vec<&TypeDecl> = Vec::new();
        let mut pending: Vec<String> = identifiers(type_expr);
        while let Some(name) = pending.pop() {
            if out.iter().any(|t| t.name == name) {
                continue;
            }
            if let Some(decl) = self.types.iter().find(|t| t.name == name) {
                out.push(decl);
                // Field types only: skip the declaration's own name and doc comment words.
                let body = decl.text.split_once(['{', '=']).map(|(_, b)| b).unwrap_or("");
                let body: String = body
                    .lines()
                    .filter(|l| !l.trim_start().starts_with("/**") && !l.trim_start().starts_with('*'))
                    .collect::<Vec<_>>()
                    .join("\n");
                let mut used = identifiers(&body);
                used.reverse();
                pending.extend(used);
            }
        }
        out
    }
}

/// Words that could be type names (`Foo[] | null` → Foo, null).
fn identifiers(text: &str) -> Vec<String> {
    text.split(|c: char| !(c.is_ascii_alphanumeric() || c == '_'))
        .filter(|w| w.chars().next().is_some_and(|c| c.is_ascii_uppercase()))
        .map(str::to_owned)
        .collect()
}

/// `  /** doc */\n  Name = 'Name',` entries of the SocketRequests enum.
fn parse_requests(ts: &str) -> Vec<(String, Option<String>)> {
    let mut out = Vec::new();
    let mut doc: Option<String> = None;
    let mut in_enum = false;
    for line in ts.lines().map(str::trim) {
        if line.starts_with("export enum SocketRequests") {
            in_enum = true;
            doc = None;
            continue;
        }
        if !in_enum {
            continue;
        }
        if line == "}" {
            break;
        }
        if let Some(d) = line.strip_prefix("/**").and_then(|d| d.strip_suffix("*/")) {
            doc = Some(d.trim().to_owned());
        } else if let Some((name, _)) = line.split_once(" = ") {
            out.push((name.trim().to_owned(), doc.take()));
        }
    }
    out
}

fn parse_types(ts: &str) -> Vec<TypeDecl> {
    let mut out = Vec::new();
    let mut comment = String::new();
    let mut lines = ts.lines();
    while let Some(line) = lines.next() {
        let trimmed = line.trim_start();
        if trimmed.starts_with("/**") {
            comment.clear();
            comment.push_str(line);
            comment.push('\n');
            let mut l = line;
            while !l.trim_end().ends_with("*/") {
                let Some(next) = lines.next() else { break };
                comment.push_str(next);
                comment.push('\n');
                l = next;
            }
            continue;
        }
        let decl = trimmed
            .strip_prefix("export interface ")
            .map(|r| (r, true))
            .or_else(|| trimmed.strip_prefix("export type ").map(|r| (r, false)));
        let Some((rest, is_interface)) = decl else {
            if !trimmed.is_empty() {
                comment.clear();
            }
            continue;
        };
        let name: String = rest.chars().take_while(|c| c.is_ascii_alphanumeric() || *c == '_').collect();
        let mut text = std::mem::take(&mut comment);
        text.push_str(line);
        text.push('\n');
        let mut last = line;
        loop {
            let done = if is_interface { last.trim_end() == "}" || last.trim_end().ends_with("{}") } else { last.trim_end().ends_with(';') };
            if done {
                break;
            }
            let Some(next) = lines.next() else { break };
            text.push_str(next);
            text.push('\n');
            last = next;
        }
        out.push(TypeDecl { name, text });
    }
    out
}

/// `  [SocketRequests.X]: Type;` lines of the SocketPayloads interface.
fn parse_payload_map(text: &str) -> Vec<(String, String)> {
    text.lines()
        .filter_map(|l| {
            let rest = l.trim().strip_prefix("[SocketRequests.")?;
            let (name, ty) = rest.split_once("]:")?;
            Some((name.to_owned(), ty.trim().trim_end_matches(';').to_owned()))
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn embedded_contract_parses() {
        let c = Catalog::load();
        let sub = c.event("subscribe").expect("Subscribe");
        assert_eq!(sub.name, "Subscribe");
        assert_eq!(sub.payload.as_deref(), Some("SubscriptionRequest"));
        assert_eq!(c.event("MediaList").unwrap().payload.as_deref(), Some("void"));
        assert!(c.event("DownloadAdded").unwrap().payload.is_none());
        let closure = c.type_closure("DownloadAddBatchRequest");
        assert!(closure.iter().any(|t| t.name == "DownloadLinkRequest"));
        assert_eq!(c.fields("RemoveMediaItemRequest"), ["id", "deleteFiles"]);
    }
}
