//! The window: check → offer (install / update, release notes) → download → install → done, or `--uninstall`:
//! confirm → remove → done. The work runs on a background thread that writes the current `Stage` into shared state
//! and asks egui to repaint.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

use eframe::egui;

use crate::install;
use crate::log::{error, info};
use crate::platform;
use crate::popcorn::PopcornBox;
use crate::uninstall;
use crate::update::{self, Check, Config, Offer, megabytes};

#[derive(Clone)]
enum Stage {
    Checking,
    UpToDate {
        version: String,
    },
    Available(Box<Offer>),
    ConfirmUninstall,
    Working {
        step: String,
        /// The whole job, 0..1.
        progress: f32,
        cancellable: bool,
    },
    Done {
        version: String,
        fresh: bool,
        warnings: Vec<String>,
    },
    Uninstalled,
    Failed {
        title: &'static str,
        message: String,
        link: Option<&'static str>,
    },
}

struct Worker {
    config: Config,
    stage: Mutex<Stage>,
    cancel: AtomicBool,
    /// The window was closed during a cancellable step: it closes once the job has stopped and cleaned up.
    closing: AtomicBool,
    ctx: egui::Context,
}

impl Worker {
    fn set(&self, stage: Stage) {
        *self.stage.lock().unwrap() = stage;
        self.ctx.request_repaint();
    }

    fn working(&self, step: &str, progress: f32, cancellable: bool) {
        self.set(Stage::Working {
            step: step.to_string(),
            progress,
            cancellable,
        });
    }

    fn start(self: &Arc<Self>) {
        if !self.config.uninstall {
            self.check();
        } else if self.config.confirm {
            self.uninstall(self.config.keep_data);
        } else {
            self.set(Stage::ConfirmUninstall);
        }
    }

    fn check(self: &Arc<Self>) {
        self.set(Stage::Checking);
        let worker = self.clone();
        std::thread::spawn(move || match update::check(&worker.config) {
            Ok(Check::UpToDate { version }) => worker.set(Stage::UpToDate { version }),
            Ok(Check::Offer(offer)) => {
                info!(
                    "Offering {} ({})",
                    offer.version,
                    if offer.fresh { "install" } else { "update" }
                );
                let missing = if offer.fresh {
                    platform::missing_runtimes()
                } else {
                    Vec::new()
                };
                if !missing.is_empty() {
                    error!("Missing: {}", missing.join(", "));
                    worker.set(Stage::Failed {
                        title: "Cannot install Popcorn yet",
                        message: platform::runtime_message(&missing),
                        link: Some(platform::RUNTIME_DOWNLOAD),
                    });
                    return;
                }
                worker.set(Stage::Available(offer.clone()));
                if worker.config.confirm {
                    worker.update(*offer, worker.config.autostart);
                }
            }
            Err(e) => worker.fail("Update check failed", e),
        });
    }

    fn update(self: &Arc<Self>, offer: Offer, autostart: bool) {
        self.cancel.store(false, Ordering::Relaxed);
        self.closing.store(false, Ordering::Relaxed);
        self.working("Preparing…", 0.0, true);
        let worker = self.clone();
        std::thread::spawn(move || {
            let report = |step: &str, progress: f32, cancellable: bool| {
                worker.working(step, progress, cancellable)
            };
            match update::run(&worker.config, &offer, autostart, &report, &worker.cancel) {
                Ok(warnings) => worker.set(Stage::Done {
                    version: offer.version,
                    fresh: offer.fresh,
                    warnings,
                }),
                Err(e) if install::is_cancelled(&e) => {
                    info!("Cancelled");
                    worker.set(Stage::Available(Box::new(offer)));
                    if worker.closing.load(Ordering::Relaxed) {
                        worker.ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                    }
                }
                Err(e) if offer.fresh => worker.fail("Installation failed", e),
                Err(e) => worker.fail("Update failed", e),
            }
        });
    }

    fn uninstall(self: &Arc<Self>, keep_data: bool) {
        self.working("Preparing…", 0.0, false);
        let worker = self.clone();
        std::thread::spawn(move || {
            let report = |step: &str, progress: f32, cancellable: bool| {
                worker.working(step, progress, cancellable)
            };
            match uninstall::uninstall(&worker.config.install_dir, keep_data, &report) {
                Ok(()) => worker.set(Stage::Uninstalled),
                Err(e) => worker.fail("Uninstall failed", e),
            }
        });
    }

    fn fail(&self, title: &'static str, e: anyhow::Error) {
        error!("{e:#}");
        self.set(Stage::Failed {
            title,
            message: format!("{e:#}"),
            link: None,
        });
    }
}

/// Points; the popcorn box of every box stage (checking, working, finished, up to date), all at `POPCORN_TOP` so it
/// does not move between them. Its kernels fly up to ~0.83 box heights above it: they stay inside the window.
const POPCORN_SIZE: f32 = 120.0;
/// Points from the top of the window to the box. Leaves under it room for three lines of text above the floating
/// buttons (busy: step, bar, percentage; finished: heading and two lines).
const POPCORN_TOP: f32 = 100.0;
/// Points; the box next to the install / update offer's heading.
const OFFER_ICON_SIZE: f32 = 52.0;
/// Points; the still box when warnings need the room (same top).
const POPCORN_SIZE_SMALL: f32 = 80.0;
/// Progress bar fill: a muted red, not the theme's blue.
const PROGRESS_FILL: egui::Color32 = egui::Color32::from_rgb(0xA8, 0x4B, 0x4B);
/// Points; above and below the row of buttons.
const BUTTONS_MARGIN: f32 = 6.0;
/// Points; the close button in the top right corner.
const CLOSE_SIZE: f32 = 28.0;

struct App {
    worker: Arc<Worker>,
    popcorn: PopcornBox,
    /// Uninstall: the "also delete the library data" box (unticked = `--keep-data`).
    delete_data: bool,
    /// Fresh install: the "Start Popcorn on system startup" box (unticked = `--no-autostart`).
    autostart: bool,
}

impl eframe::App for App {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let ctx = ui.ctx().clone();
        let stage = self.worker.stage.lock().unwrap().clone();

        // Closing the window must not abandon a half-done swap or removal. Earlier steps are cancelled instead: the
        // worker cleans up and closes the window then.
        let (busy, cancellable) = match stage {
            Stage::Working { cancellable, .. } => (!cancellable, cancellable),
            _ => (false, false),
        };
        if (busy || cancellable) && ctx.input(|i| i.viewport().close_requested()) {
            ctx.send_viewport_cmd(egui::ViewportCommand::CancelClose);
            if cancellable {
                info!("Window closed, cancelling");
                self.worker.closing.store(true, Ordering::Relaxed);
                self.worker.cancel.store(true, Ordering::Relaxed);
            }
        }

        // No title bar: the whole window drags it. Registered first, so every widget on top keeps its own clicks.
        let window = ui.max_rect();
        let background = ui.interact(window, egui::Id::unique("window-drag"), egui::Sense::drag());
        // egui hands the drag to the background even when the press is on a button (buttons only sense clicks), and
        // on the press frame already: the compositor's move grab then swallows the release, so the click got lost
        // whenever the pointer was not let go within the same frame. Presses on clickable widgets never move the window.
        if background.drag_started_by(egui::PointerButton::Primary) && !pointer_on_clickable(&ctx, background.id) {
            ctx.send_viewport_cmd(egui::ViewportCommand::StartDrag);
        }

        // Busy stages are cancelled with the close button: no buttons.
        let has_buttons = match &stage {
            Stage::Checking | Stage::Working { .. } | Stage::Uninstalled => false,
            // Started by Popcorn: it restarts what ran, nothing to open.
            Stage::Done { .. } => !self.worker.config.from_app,
            _ => true,
        };
        // Stages with the popcorn box keep the whole window, their buttons float over its bottom: the box stays put
        // whether there are buttons or not. The others give the buttons a row of their own.
        let floating = matches!(
            stage,
            Stage::Checking
                | Stage::Working { .. }
                | Stage::Done { .. }
                | Stage::UpToDate { .. }
                | Stage::Uninstalled
        );
        let row_height = ui.spacing().interact_size.y;
        if has_buttons && !floating {
            egui::Panel::bottom("buttons")
                .resizable(false)
                .show_separator_line(false)
                .show(ui, |ui| {
                    ui.add_space(BUTTONS_MARGIN);
                    // One row: a bare right-to-left layout would take the panel's whole height.
                    let row = egui::vec2(ui.available_width(), row_height);
                    ui.allocate_ui_with_layout(
                        row,
                        egui::Layout::right_to_left(egui::Align::Center),
                        |ui| self.buttons(ui, &ctx, &stage),
                    );
                    ui.add_space(BUTTONS_MARGIN);
                });
        }
        egui::CentralPanel::default_margins().show(ui, |ui| self.body(ui, &stage));
        if has_buttons && floating {
            // Where the panel's row would be.
            let row = egui::Rect::from_min_max(
                egui::pos2(
                    window.left() + 8.0,
                    window.bottom() - 2.0 - BUTTONS_MARGIN - row_height,
                ),
                egui::pos2(window.right() - 8.0, window.bottom() - 2.0 - BUTTONS_MARGIN),
            );
            ui.scope_builder(
                egui::UiBuilder::new()
                    .max_rect(row)
                    .layout(egui::Layout::right_to_left(egui::Align::Center)),
                |ui| self.buttons(ui, &ctx, &stage),
            );
        }

        let close = egui::Rect::from_min_size(
            egui::pos2(window.right() - CLOSE_SIZE - 6.0, window.top() + 6.0),
            egui::Vec2::splat(CLOSE_SIZE),
        );
        if close_button(ui, close, !busy).clicked() {
            ctx.send_viewport_cmd(egui::ViewportCommand::Close);
        }
        // Edge of the borderless window.
        ui.painter().rect_stroke(
            window,
            0.0,
            ui.visuals().window_stroke,
            egui::StrokeKind::Inside,
        );
    }
}

/// Whether the pointer is over a widget other than `background` that senses clicks (buttons, checkboxes, ✕).
fn pointer_on_clickable(ctx: &egui::Context, background: egui::Id) -> bool {
    let under_pointer: Vec<egui::Id> = ctx.interaction_snapshot(|s| s.contains_pointer.iter().copied().collect());
    under_pointer
        .into_iter()
        .filter(|id| *id != background)
        .any(|id| ctx.read_response(id).is_some_and(|r| r.enabled() && r.sense.senses_click()))
}

/// ✕ with a hover background, like a title bar's close button.
fn close_button(ui: &mut egui::Ui, rect: egui::Rect, enabled: bool) -> egui::Response {
    let sense = if enabled {
        egui::Sense::click()
    } else {
        egui::Sense::hover()
    };
    let response = ui
        .interact(rect, egui::Id::unique("window-close"), sense)
        .on_hover_cursor(egui::CursorIcon::Default);
    let visuals = ui.visuals();
    let color = if !enabled {
        visuals
            .widgets
            .noninteractive
            .fg_stroke
            .color
            .gamma_multiply(0.4)
    } else if response.hovered() {
        egui::Color32::WHITE
    } else {
        visuals.widgets.inactive.fg_stroke.color
    };
    if enabled && response.hovered() {
        let fill = if response.is_pointer_button_down_on() {
            egui::Color32::from_rgb(0xA0, 0x1A, 0x1A)
        } else {
            egui::Color32::from_rgb(0xC4, 0x2B, 0x1C)
        };
        ui.painter().rect_filled(rect, 4.0, fill);
    }
    let cross = rect.shrink(rect.width() * 0.33);
    let stroke = egui::Stroke::new(1.5, color);
    let painter = ui.painter();
    painter.line_segment([cross.left_top(), cross.right_bottom()], stroke);
    painter.line_segment([cross.right_top(), cross.left_bottom()], stroke);
    response
}

/// A version number in a rounded tag: the new one filled with the accent, the installed one muted.
fn version_pill(ui: &mut egui::Ui, version: &str, new: bool) {
    let (fill, text) = if new {
        (PROGRESS_FILL, egui::Color32::WHITE)
    } else {
        (
            ui.visuals().widgets.inactive.bg_fill,
            ui.visuals().weak_text_color(),
        )
    };
    egui::Frame::new()
        .fill(fill)
        .corner_radius(8)
        .inner_margin(egui::Margin::symmetric(7, 1))
        .show(ui, |ui| {
            ui.label(egui::RichText::new(version).color(text).strong());
        });
}

/// GitHub release notes, the common markdown only: headings, bullets, emphasis / code marks and links dropped to
/// their text. The title is left out when it only repeats the version tag.
fn release_notes(ui: &mut egui::Ui, title: &str, notes: &str) {
    let title = title.trim();
    if !title.is_empty()
        && !title
            .trim_start_matches(['v', 'V'])
            .starts_with(char::is_numeric)
    {
        ui.label(egui::RichText::new(title).strong().size(15.0));
        ui.add_space(4.0);
    }
    if notes.trim().is_empty() {
        ui.weak("No release notes.");
        return;
    }
    let mut blank = false;
    for line in notes.lines() {
        let line = line.trim_end();
        if line.trim().is_empty() {
            blank = true;
            continue;
        }
        if blank {
            ui.add_space(4.0);
            blank = false;
        }
        let indent = line.len() - line.trim_start().len();
        let line = line.trim_start();
        if let Some(heading) = line.strip_prefix('#') {
            ui.add_space(2.0);
            ui.label(egui::RichText::new(plain(heading.trim_start_matches('#').trim())).strong());
        } else if let Some(item) = line.strip_prefix("- ").or_else(|| line.strip_prefix("* ")) {
            ui.horizontal_top(|ui| {
                ui.add_space(4.0 + 8.0 * (indent / 2) as f32);
                ui.weak("•");
                ui.add(egui::Label::new(plain(item)).wrap());
            });
        } else {
            ui.add(egui::Label::new(plain(line)).wrap());
        }
    }
}

/// Markdown inline marks removed: `**bold**`, `__bold__`, `` `code` ``, `[text](url)` → text.
fn plain(text: &str) -> String {
    let text = text.replace("**", "").replace("__", "").replace('`', "");
    let mut out = String::with_capacity(text.len());
    let mut rest = text.as_str();
    while let Some(open) = rest.find('[') {
        let Some(close) = rest[open..].find("](").map(|i| open + i) else {
            break;
        };
        let Some(end) = rest[close..].find(')').map(|i| close + i) else {
            break;
        };
        out.push_str(&rest[..open]);
        out.push_str(&rest[open + 1..close]);
        rest = &rest[end + 1..];
    }
    out.push_str(rest);
    out
}

impl App {
    /// The popcorn box (`size` points; bouncing while busy, `still` once finished), its top `POPCORN_TOP` below the
    /// window's on every stage, with `below` centred under it.
    fn with_box(
        &mut self,
        ui: &mut egui::Ui,
        size: f32,
        still: bool,
        below: impl FnOnce(&mut egui::Ui),
    ) {
        // The viewport starts at 0: the cursor is the distance from the window's top.
        ui.add_space((POPCORN_TOP - ui.cursor().top()).max(0.0));
        if still {
            self.popcorn.show_still(ui, size);
        } else {
            self.popcorn.show(ui, size);
        }
        ui.add_space(12.0);
        ui.vertical_centered(below);
    }

    fn body(&mut self, ui: &mut egui::Ui, stage: &Stage) {
        let install_dir = self.worker.config.install_dir.display().to_string();
        ui.add_space(4.0);
        match stage {
            Stage::Checking => self.with_box(ui, POPCORN_SIZE, false, |ui| {
                ui.label("Checking for updates…");
                // Room of the working stage's bar and percentage: the box stays where it will be then.
                ui.add_visible(false, egui::ProgressBar::new(0.0));
                ui.add_visible(false, egui::Label::new("0 %"));
            }),
            Stage::UpToDate { version } => {
                let version = version.clone();
                self.with_box(ui, POPCORN_SIZE, true, |ui| {
                    ui.heading("You're up to date");
                    ui.add_space(4.0);
                    let mut text = egui::text::LayoutJob::default();
                    let style = ui.style();
                    egui::RichText::new("Popcorn ").append_to(
                        &mut text,
                        style,
                        egui::FontSelection::Default,
                        egui::Align::Center,
                    );
                    egui::RichText::new(&version).strong().append_to(
                        &mut text,
                        style,
                        egui::FontSelection::Default,
                        egui::Align::Center,
                    );
                    egui::RichText::new(" is the latest version.").append_to(
                        &mut text,
                        style,
                        egui::FontSelection::Default,
                        egui::Align::Center,
                    );
                    ui.label(text);
                });
            }
            Stage::Available(offer) => {
                // Header: box icon, heading, versions (installed → new) and download size.
                ui.horizontal(|ui| {
                    self.popcorn.show_icon(ui, OFFER_ICON_SIZE);
                    ui.add_space(6.0);
                    ui.vertical(|ui| {
                        ui.add_space(4.0);
                        ui.heading(if offer.fresh {
                            "Install Popcorn"
                        } else if offer.is_file() {
                            "Install this Popcorn package"
                        } else {
                            "A Popcorn update is available"
                        });
                        ui.add_space(2.0);
                        ui.horizontal(|ui| {
                            ui.spacing_mut().item_spacing.x = 6.0;
                            if !offer.fresh {
                                let installed = offer.installed.as_deref().unwrap_or("unknown");
                                version_pill(ui, installed, false);
                                ui.weak("→");
                            }
                            version_pill(ui, &offer.version, true);
                            if let Some(size) = offer.size {
                                ui.weak(format!("· {} download", megabytes(size)));
                            }
                        });
                    });
                });
                ui.add_space(10.0);

                // Below the notes: what happens / the install options.
                let footer = if offer.fresh { 62.0 } else { 22.0 };
                egui::Frame::new()
                    .fill(ui.visuals().extreme_bg_color)
                    .stroke(ui.visuals().widgets.noninteractive.bg_stroke)
                    .corner_radius(6)
                    .inner_margin(egui::Margin::symmetric(12, 10))
                    .show(ui, |ui| {
                        egui::ScrollArea::vertical()
                            .auto_shrink(false)
                            .max_height((ui.available_height() - footer).max(48.0))
                            .show(ui, |ui| release_notes(ui, &offer.title, &offer.notes));
                    });
                ui.add_space(8.0);
                if offer.fresh {
                    ui.weak(format!("Installs into {install_dir}, with FFmpeg."));
                    ui.add_space(4.0);
                    ui.checkbox(&mut self.autostart, "Start Popcorn on system startup")
                        .on_hover_text(platform::TEXTS.autostart_hint);
                } else {
                    ui.weak("Popcorn closes during the update and starts again afterwards.");
                }
            }
            Stage::ConfirmUninstall => {
                ui.heading("Uninstall Popcorn?");
                ui.label(format!(
                    "Removes the program from {install_dir}, {}.",
                    platform::TEXTS.uninstall_removes
                ));
                ui.add_space(8.0);
                ui.checkbox(
                    &mut self.delete_data,
                    "Also delete the library data (database, settings, plugins, caches)",
                );
                ui.small("Downloaded media files stay.");
            }
            Stage::Working {
                step,
                progress,
                cancellable,
            } => {
                let step = if *cancellable && self.worker.closing.load(Ordering::Relaxed) {
                    "Cancelling…".to_string()
                } else {
                    step.clone()
                };
                let progress = *progress;
                self.with_box(ui, POPCORN_SIZE, false, |ui| {
                    ui.label(step);
                    let bar = egui::ProgressBar::new(progress).fill(PROGRESS_FILL);
                    ui.add(bar.desired_width(ui.available_width() * 0.8));
                    ui.label(format!("{:.0} %", progress * 100.0));
                });
            }
            Stage::Done {
                version,
                fresh,
                warnings,
            } => {
                let (version, fresh, warnings) = (version.clone(), *fresh, warnings.clone());
                let autostart = self.autostart;
                let size = if warnings.is_empty() {
                    POPCORN_SIZE
                } else {
                    POPCORN_SIZE_SMALL
                };
                self.with_box(ui, size, true, |ui| {
                    ui.heading(if fresh {
                        "Popcorn is installed"
                    } else {
                        "Update complete"
                    });
                    ui.label(format!("Version {version}."));
                    if fresh {
                        let launcher = platform::TEXTS.launcher;
                        ui.label(if autostart {
                            format!("It starts when you sign in, or open it from {launcher}.")
                        } else {
                            format!("Open it from {launcher}.")
                        });
                    }
                    for warning in &warnings {
                        ui.add_space(6.0);
                        ui.colored_label(ui.visuals().warn_fg_color, warning);
                    }
                });
            }
            Stage::Uninstalled => {
                let delete_data = self.delete_data;
                self.with_box(ui, POPCORN_SIZE, true, |ui| {
                    ui.heading("Popcorn was removed");
                    ui.label(if delete_data {
                        "The program and its library data is removed."
                    } else {
                        "The program is removed, but kept the library data."
                    });
                });
            }
            Stage::Failed {
                title,
                message,
                link,
            } => {
                ui.heading(*title);
                egui::ScrollArea::vertical()
                    .auto_shrink(false)
                    .max_height(ui.available_height() - 48.0)
                    .show(ui, |ui| {
                        ui.colored_label(ui.visuals().error_fg_color, message);
                        if let Some(link) = link {
                            ui.hyperlink(*link);
                        }
                    });
                if let Some(log) = crate::log::file_path() {
                    ui.small(format!("Log: {}", log.display()));
                }
            }
        }
    }

    fn buttons(&self, ui: &mut egui::Ui, ctx: &egui::Context, stage: &Stage) {
        let close = |ui: &mut egui::Ui, text: &str| {
            if ui
                .add(egui::Button::new(text).min_size(egui::vec2(88.0, 0.0)))
                .clicked()
            {
                ctx.send_viewport_cmd(egui::ViewportCommand::Close);
            }
        };
        match stage {
            Stage::Checking | Stage::Working { .. } | Stage::Uninstalled => {}
            Stage::Done { .. } => {
                // Right to left: the app (the main action) rightmost. The server runs already (install.rs).
                if ui.add(accent("Open Popcorn")).clicked() {
                    platform::start_app(&self.worker.config.install_dir);
                    ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                }
                if ui
                    .add(egui::Button::new("Open in browser").min_size(egui::vec2(88.0, 0.0)))
                    .clicked()
                {
                    platform::open_browser();
                    ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                }
            }
            Stage::UpToDate { .. } => {
                if ui.button("Check again").clicked() {
                    self.worker.check();
                }
            }
            Stage::Available(offer) => {
                let action = if offer.fresh || offer.is_file() {
                    "Install"
                } else {
                    "Update"
                };
                // Right to left: the action left of Cancel (Windows order). An update has only the action: the
                // close button is "later".
                if offer.fresh {
                    close(ui, "Cancel");
                }
                if ui.add(accent(action)).clicked() {
                    self.worker.update((**offer).clone(), self.autostart);
                }
            }
            Stage::ConfirmUninstall => {
                close(ui, "Cancel");
                if ui.button("Uninstall").clicked() {
                    self.worker.uninstall(!self.delete_data);
                }
            }
            Stage::Failed { .. } => {
                if ui.button("Retry").clicked() {
                    if self.worker.config.uninstall {
                        self.worker.set(Stage::ConfirmUninstall);
                    } else {
                        self.worker.check();
                    }
                }
            }
        }
    }
}

pub fn run(config: Config) -> anyhow::Result<()> {
    let title = if config.uninstall {
        "Uninstall Popcorn"
    } else {
        "Popcorn Installer"
    };
    let mut viewport = egui::ViewportBuilder::default()
        .with_title(title)
        // Wayland has no window icon in winit: compositors take it from the desktop entry named like the app_id, so
        // use the app's (`popcorn.desktop`, written on install). `with_icon` below covers Windows and X11.
        .with_app_id("popcorn")
        .with_inner_size([480.0, 340.0])
        .with_resizable(false)
        // The app draws everything itself: no title bar or frame (see App::ui).
        .with_decorations(false);
    if let Ok(icon) =
        eframe::icon_data::from_png_bytes(include_bytes!("../../Popcorn.Linux/popcorn.png"))
    {
        viewport = viewport.with_icon(icon);
    }
    let options = eframe::NativeOptions {
        viewport,
        centered: true,
        ..Default::default()
    };

    eframe::run_native(
        title,
        options,
        Box::new(move |cc| {
            let delete_data = !config.keep_data;
            let autostart = config.autostart;
            let worker = Arc::new(Worker {
                config,
                stage: Mutex::new(Stage::Checking),
                cancel: AtomicBool::new(false),
                closing: AtomicBool::new(false),
                ctx: cc.egui_ctx.clone(),
            });
            // Selectable text would take the drags that move the window.
            cc.egui_ctx
                .global_style_mut(|style| style.interaction.selectable_labels = false);
            worker.start();
            Ok(Box::new(App {
                worker,
                popcorn: PopcornBox::new(&cc.egui_ctx),
                delete_data,
                autostart,
            }))
        }),
    )
    .map_err(|e| anyhow::anyhow!("{e}"))
}

/// The main action's button: filled with the progress bar's colour.
fn accent(text: &str) -> egui::Button<'_> {
    egui::Button::new(
        egui::RichText::new(text)
            .color(egui::Color32::WHITE)
            .strong(),
    )
    .fill(PROGRESS_FILL)
    .min_size(egui::vec2(88.0, 0.0))
}
