//! Single-line text input with a cursor and (optional) history.

use ratatui::crossterm::event::{KeyCode, KeyEvent, KeyModifiers};

pub enum InputAction {
    None,
    Submit(String),
    Cancel,
    /// Tab: the owner completes (`line::complete`).
    Complete,
}

#[derive(Default)]
pub struct LineInput {
    pub text: String,
    /// Char index.
    pub cursor: usize,
    history: Vec<String>,
    history_pos: Option<usize>,
}

impl LineInput {
    pub fn with_text(text: &str) -> Self {
        Self { text: text.to_owned(), cursor: text.chars().count(), ..Default::default() }
    }

    fn byte_at(&self, char_index: usize) -> usize {
        self.text.char_indices().nth(char_index).map_or(self.text.len(), |(i, _)| i)
    }

    /// Text left of the cursor.
    pub fn before_cursor(&self) -> &str {
        &self.text[..self.byte_at(self.cursor)]
    }

    /// Replaces `before_cursor()[start..]` with `replacement`.
    pub fn replace_word(&mut self, start: usize, replacement: &str) {
        let end = self.byte_at(self.cursor);
        self.text.replace_range(start..end, replacement);
        self.cursor = self.text[..start + replacement.len()].chars().count();
    }

    pub fn key(&mut self, key: KeyEvent) -> InputAction {
        let ctrl = key.modifiers.contains(KeyModifiers::CONTROL);
        let len = self.text.chars().count();
        match key.code {
            KeyCode::Enter => {
                let text = std::mem::take(&mut self.text);
                self.cursor = 0;
                self.history_pos = None;
                if !text.trim().is_empty() && self.history.last() != Some(&text) {
                    self.history.push(text.clone());
                }
                return InputAction::Submit(text);
            }
            KeyCode::Esc => return InputAction::Cancel,
            KeyCode::Tab => return InputAction::Complete,
            KeyCode::Char('a') if ctrl => self.cursor = 0,
            KeyCode::Char('e') if ctrl => self.cursor = len,
            KeyCode::Char('u') if ctrl => {
                let at = self.byte_at(self.cursor);
                self.text.replace_range(..at, "");
                self.cursor = 0;
            }
            KeyCode::Char('w') if ctrl => {
                let before = self.before_cursor();
                let start = before.trim_end().rfind(' ').map_or(0, |i| i + 1);
                let removed = before[start..].chars().count();
                let at = self.byte_at(self.cursor);
                self.text.replace_range(start..at, "");
                self.cursor -= removed;
            }
            KeyCode::Char(c) if !ctrl => {
                let at = self.byte_at(self.cursor);
                self.text.insert(at, c);
                self.cursor += 1;
            }
            KeyCode::Backspace if self.cursor > 0 => {
                let at = self.byte_at(self.cursor - 1);
                self.text.remove(at);
                self.cursor -= 1;
            }
            KeyCode::Delete if self.cursor < len => {
                let at = self.byte_at(self.cursor);
                self.text.remove(at);
            }
            KeyCode::Left => self.cursor = self.cursor.saturating_sub(1),
            KeyCode::Right => self.cursor = (self.cursor + 1).min(len),
            KeyCode::Home => self.cursor = 0,
            KeyCode::End => self.cursor = len,
            KeyCode::Up if !self.history.is_empty() => {
                let pos = self.history_pos.map_or(self.history.len() - 1, |p| p.saturating_sub(1));
                self.history_pos = Some(pos);
                self.set(self.history[pos].clone());
            }
            KeyCode::Down => match self.history_pos {
                Some(p) if p + 1 < self.history.len() => {
                    self.history_pos = Some(p + 1);
                    self.set(self.history[p + 1].clone());
                }
                Some(_) => {
                    self.history_pos = None;
                    self.set(String::new());
                }
                None => {}
            },
            _ => {}
        }
        InputAction::None
    }

    fn set(&mut self, text: String) {
        self.cursor = text.chars().count();
        self.text = text;
    }
}
