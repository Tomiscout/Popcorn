//! Command lines typed by hand (repl prompt, TUI console): word splitting and Tab completion.

use crate::catalog::Catalog;

pub const TOPICS: [&str; 3] = ["DownloadList", "DownloadStats", "SegmentsState"];

/// Shell-like words: whitespace separated, '…' and "…" quoting, backslash escapes outside single quotes.
pub fn split_words(line: &str) -> Result<Vec<String>, String> {
    let mut words = Vec::new();
    let mut word = String::new();
    let mut in_word = false;
    let mut quote: Option<char> = None;
    let mut chars = line.chars();
    while let Some(c) = chars.next() {
        match (quote, c) {
            (Some(q), c) if c == q => quote = None,
            (Some('"'), '\\') | (None, '\\') => {
                if let Some(next) = chars.next() {
                    word.push(next);
                }
                in_word = true;
            }
            (Some(_), c) => word.push(c),
            (None, '\'' | '"') => {
                quote = Some(c);
                in_word = true;
            }
            (None, c) if c.is_whitespace() => {
                if in_word {
                    words.push(std::mem::take(&mut word));
                    in_word = false;
                }
            }
            (None, c) => {
                word.push(c);
                in_word = true;
            }
        }
    }
    if quote.is_some() {
        return Err("unclosed quote".into());
    }
    if in_word {
        words.push(word);
    }
    Ok(words)
}

/// `listen <Topic> [key]` / `unlisten …` → `{ topic, key }` (topic spelled as the server does).
pub fn subscription(args: &[String]) -> Option<serde_json::Value> {
    match args {
        [topic, key @ ..] if key.len() <= 1 => {
            let topic = TOPICS.iter().find(|t| t.eq_ignore_ascii_case(topic)).map_or(topic.as_str(), |t| t);
            Some(serde_json::json!({ "topic": topic, "key": key.first() }))
        }
        _ => None,
    }
}

pub struct Candidate {
    pub display: String,
    pub replacement: String,
}

/// Completions for the word ending at the end of `before`: (start of that word, candidates). First word = one of
/// `builtins` or an event the server handles; then topics, on/off, or the payload's field names (`key=`).
pub fn complete(catalog: &Catalog, builtins: &[&str], before: &str) -> (usize, Vec<Candidate>) {
    let start = before.rfind(char::is_whitespace).map_or(0, |i| i + 1);
    let prefix = &before[start..];
    let previous: Vec<&str> = before[..start].split_whitespace().collect();
    let starts = |s: &str| s.len() >= prefix.len() && s.is_char_boundary(prefix.len()) && s[..prefix.len()].eq_ignore_ascii_case(prefix);
    let pair = |s: &str, append: &str| Candidate { display: s.to_owned(), replacement: format!("{s}{append}") };
    let is = |w: &&str, names: &[&str]| names.iter().any(|n| w.eq_ignore_ascii_case(n));

    let candidates = match previous.as_slice() {
        [] => builtins
            .iter()
            .copied()
            .chain(catalog.events.iter().filter(|e| e.payload.is_some()).map(|e| e.name.as_str()))
            .filter(|s| starts(s))
            .map(|s| pair(s, " "))
            .collect(),
        [cmd] if is(cmd, &["describe"]) && builtins.contains(&"describe") => {
            catalog.events.iter().map(|e| e.name.as_str()).filter(|s| starts(s)).map(|s| pair(s, "")).collect()
        }
        [cmd] if is(cmd, &["listen", "unlisten"]) && builtins.contains(&"listen") => {
            TOPICS.iter().copied().filter(|s| starts(s)).map(|s| pair(s, " ")).collect()
        }
        [cmd] if is(cmd, &["pretty", "payload"]) && builtins.contains(&"pretty") => {
            ["on", "off"].into_iter().filter(|s| starts(s)).map(|s| pair(s, "")).collect()
        }
        [event, ..] if !prefix.contains('=') => match catalog.event(event).and_then(|e| e.payload.as_deref()) {
            Some(ty) => catalog.fields(ty).iter().filter(|f| starts(f)).map(|f| pair(f, "=")).collect(),
            None => Vec::new(),
        },
        _ => Vec::new(),
    };
    (start, candidates)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn words() {
        assert_eq!(split_words(r#"MediaItem id=abc"#).unwrap(), ["MediaItem", "id=abc"]);
        assert_eq!(split_words(r#"X '{"a": 1}' b="c d" e\ f"#).unwrap(), ["X", r#"{"a": 1}"#, "b=c d", "e f"]);
        assert_eq!(split_words(r#"X '' "#).unwrap(), ["X", ""]);
        assert!(split_words("X 'open").is_err());
    }

    #[test]
    fn completion() {
        let c = Catalog::load();
        let (start, got) = complete(&c, &[], "RemoveMediaItem de");
        assert_eq!(start, 16);
        assert_eq!(got.iter().map(|c| c.replacement.as_str()).collect::<Vec<_>>(), ["deleteFiles="]);
        let (_, got) = complete(&c, &["listen"], "lis");
        assert_eq!(got[0].replacement, "listen ");
    }
}
