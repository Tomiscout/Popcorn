//! The bouncing popcorn box of the web UI's loading page (`Popcorn.Web/src/components/popcorn-box`): the box pulses,
//! tosses up on every pop and kernels fly out of it under gravity, fading after a moment. The web version pops on
//! click; here it pops by itself (and on click). Sizes and speeds are the CSS ones scaled from its 256 px box.

use eframe::egui::emath::Rot2;
use eframe::egui::{self, Color32, Mesh, Pos2, Rect, Sense, TextureHandle, Vec2, pos2, vec2};

/// Box size of the web page (the 256 px image in popcorn.ico), the reference for every length below.
const WEB_BOX: f32 = 256.0;
const GRAVITY: f32 = 3000.0;
const PULSE_SECONDS: f64 = 1.5;
const TOSS_SECONDS: f64 = 0.35;
/// Kernel fully visible this long, then fades out over `FADE_SECONDS`.
const FADE_DELAY: f32 = 0.3;
const FADE_SECONDS: f32 = 0.6;
/// Seconds between automatic pops.
const POP_INTERVAL: (f64, f64) = (0.9, 2.0);

struct Kernel {
    /// Relative to the box's top left corner, in web pixels.
    pos: Vec2,
    velocity: Vec2,
    rotation: f32,
    age: f32,
}

pub struct PopcornBox {
    box_texture: TextureHandle,
    kernel_texture: TextureHandle,
    kernels: Vec<Kernel>,
    last_frame: Option<f64>,
    next_pop: f64,
    toss_started: Option<f64>,
    seed: u64,
}

impl PopcornBox {
    pub fn new(ctx: &egui::Context) -> Self {
        Self {
            box_texture: texture(
                ctx,
                "popcorn-box",
                include_bytes!("../../Popcorn.Linux/popcorn.png"),
                256,
            ),
            kernel_texture: texture(
                ctx,
                "popcorn-kernel",
                include_bytes!("../../Popcorn.Web/public/images/pop_1.png"),
                64,
            ),
            kernels: Vec::new(),
            last_frame: None,
            next_pop: 0.0,
            toss_started: None,
            seed: 0x9E37_79B9_7F4A_7C15,
        }
    }

    /// Box `size` points wide, centred in a row of its height (the kernels fly above it, unclipped).
    pub fn show(&mut self, ui: &mut egui::Ui, size: f32) {
        let scale = size / WEB_BOX;
        let (row, response) =
            ui.allocate_exact_size(vec2(ui.available_width(), size), Sense::click());
        let box_rect = Rect::from_center_size(
            pos2(row.center().x, row.bottom() - size / 2.0),
            Vec2::splat(size),
        );

        let now = ui.input(|i| i.time);
        let dt = self
            .last_frame
            .map_or(0.0, |last| (now - last).clamp(0.0, 0.05) as f32);
        self.last_frame = Some(now);

        if now >= self.next_pop || response.clicked() {
            self.pop(now);
        }
        self.step(dt);

        // Box: pulse (scale 1 → 1.05, opacity 0.7 → 1) inside the toss (up, drop with overshoot, slight tilt
        // around 50 % / 85 %).
        let pulse = (1.0 - (now * std::f64::consts::TAU / PULSE_SECONDS).cos()) as f32 / 2.0;
        let (lift, tilt) = self.toss(now);
        let painter = ui.painter_at(ui.clip_rect());
        let tossed = box_rect.translate(vec2(0.0, lift * scale));
        let pivot = pos2(tossed.center().x, tossed.top() + tossed.height() * 0.85);
        let pulsed = Rect::from_center_size(tossed.center(), tossed.size() * (1.0 + 0.05 * pulse));
        painter.add(quad(
            &self.box_texture,
            pulsed,
            Rot2::from_angle(tilt.to_radians()),
            pivot,
            0.7 + 0.3 * pulse,
        ));

        // Kernels on top, not clipped to the row: they fly above it like on the web page.
        let layer = egui::LayerId::new(
            egui::Order::Foreground,
            ui.scope_id().with("popcorn-kernels"),
        );
        let kernels = ui.ctx().layer_painter(layer);
        let kernel_size = Vec2::splat(72.0 * scale);
        for kernel in &self.kernels {
            let center = box_rect.min + kernel.pos * scale;
            let rect = Rect::from_center_size(center, kernel_size);
            let opacity = 1.0 - ((kernel.age - FADE_DELAY) / FADE_SECONDS).clamp(0.0, 1.0);
            kernels.add(quad(
                &self.kernel_texture,
                rect,
                Rot2::from_angle(kernel.rotation),
                center,
                opacity,
            ));
        }

        ui.ctx().request_repaint();
    }

    /// Just the box, `size` points square (an icon next to other content).
    pub fn show_icon(&self, ui: &mut egui::Ui, size: f32) {
        let (rect, _) = ui.allocate_exact_size(Vec2::splat(size), Sense::hover());
        ui.painter().add(quad(
            &self.box_texture,
            rect,
            Rot2::IDENTITY,
            rect.center(),
            1.0,
        ));
    }

    /// The box at rest, like `show` but without animation (finished stages): no pulse, toss or kernels.
    pub fn show_still(&mut self, ui: &mut egui::Ui, size: f32) {
        let (row, _) = ui.allocate_exact_size(vec2(ui.available_width(), size), Sense::hover());
        let box_rect = Rect::from_center_size(
            pos2(row.center().x, row.bottom() - size / 2.0),
            Vec2::splat(size),
        );
        self.kernels.clear();
        self.last_frame = None;
        ui.painter().add(quad(
            &self.box_texture,
            box_rect,
            Rot2::IDENTITY,
            box_rect.center(),
            1.0,
        ));
    }

    /// One kernel out of the top of the box (the web page's click into the box).
    fn pop(&mut self, now: f64) {
        let x = WEB_BOX * 0.5 + (self.random() - 0.5) * WEB_BOX * 0.35;
        let kernel = Kernel {
            pos: vec2(x, WEB_BOX * 0.1),
            velocity: vec2(
                (self.random() - 0.5) * 300.0,
                -900.0 - self.random() * 200.0,
            ),
            rotation: self.random() * std::f32::consts::TAU,
            age: 0.0,
        };
        self.kernels.push(kernel);
        self.toss_started = Some(now);
        let (min, max) = POP_INTERVAL;
        self.next_pop = now + min + (max - min) * self.random() as f64;
    }

    fn step(&mut self, dt: f32) {
        for kernel in &mut self.kernels {
            kernel.pos += kernel.velocity * dt;
            kernel.velocity.y += GRAVITY * dt;
            kernel.age += dt;
        }
        self.kernels
            .retain(|k| k.age < FADE_DELAY + FADE_SECONDS && k.pos.y < 600.0);
    }

    /// `shake-rotate` keyframes: (web px up/down, degrees).
    fn toss(&self, now: f64) -> (f32, f32) {
        const KEYS: [(f32, f32, f32); 5] = [
            (0.0, 0.0, 0.0),
            (0.25, -16.0, -2.0),
            (0.55, 3.0, 1.5),
            (0.75, -2.0, -0.5),
            (1.0, 0.0, 0.0),
        ];
        let Some(started) = self.toss_started else {
            return (0.0, 0.0);
        };
        let t = ((now - started) / TOSS_SECONDS) as f32;
        if t >= 1.0 {
            return (0.0, 0.0);
        }
        // ease-out over the whole animation, linear between the keyframes.
        let t = 1.0 - (1.0 - t).powi(2);
        let i = KEYS
            .iter()
            .rposition(|k| k.0 <= t)
            .unwrap_or(0)
            .min(KEYS.len() - 2);
        let (a, b) = (KEYS[i], KEYS[i + 1]);
        let f = (t - a.0) / (b.0 - a.0);
        (a.1 + (b.1 - a.1) * f, a.2 + (b.2 - a.2) * f)
    }

    /// 0..1, xorshift: only for looks.
    fn random(&mut self) -> f32 {
        self.seed ^= self.seed << 13;
        self.seed ^= self.seed >> 7;
        self.seed ^= self.seed << 17;
        (self.seed >> 40) as f32 / (1u64 << 24) as f32
    }
}

/// Texture `side` px wide: scaled down once here, the renderer has no mipmaps everywhere.
fn texture(ctx: &egui::Context, name: &str, png: &[u8], side: u32) -> TextureHandle {
    let image = image::load_from_memory_with_format(png, image::ImageFormat::Png)
        .expect("bundled PNG")
        .resize(side, side, image::imageops::FilterType::Triangle)
        .into_rgba8();
    let size = [image.width() as usize, image.height() as usize];
    let pixels = egui::ColorImage::from_rgba_unmultiplied(size, image.as_raw());
    ctx.load_texture(name, pixels, egui::TextureOptions::LINEAR)
}

fn quad(texture: &TextureHandle, rect: Rect, rotation: Rot2, pivot: Pos2, opacity: f32) -> Mesh {
    let mut mesh = Mesh::with_texture(texture.id());
    let uv = Rect::from_min_max(Pos2::ZERO, pos2(1.0, 1.0));
    mesh.add_rect_with_uv(rect, uv, Color32::WHITE.gamma_multiply(opacity));
    mesh.rotate(rotation, pivot);
    mesh
}
