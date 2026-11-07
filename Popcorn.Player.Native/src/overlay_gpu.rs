//! GPU path for the CEF overlay (CefWindowInfo::shared_texture_enabled).
//!
//! CEF hands every frame over as a DMA-BUF from its GPU process (OnAcceleratedPaint), but only
//! for the duration of the callback: the buffer goes back to Chromium's pool afterwards. So the
//! callback copies it on the GPU (EGL/GLES on the compositor's GPU, blit between two imported
//! DMA-BUFs) into one of our own GBM buffers, which the Wayland thread then attaches to the main
//! surface. No pixels pass through the CPU.
//!
//! Buffer life cycle (`Slot::state`, all transitions under `Pool`'s lock):
//!   Free --CEF thread--> Writing --copy done--> Ready --Wayland thread attach--> Attached
//!   Attached --wl_buffer.release--> Free;  Ready --newer frame--> Writing (superseded)
//! GL objects belong to the CEF thread, wl_buffers to the Wayland thread (created there; only
//! destroyed elsewhere under the lock, when the overlay size changes).

use std::ffi::CStr;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, OnceLock};

use cef::{AcceleratedPaintInfo, ImplBrowserHost, Rect};
use wayland_client::protocol::{wl_buffer::WlBuffer, wl_surface::WlSurface};
use wayland_client::QueueHandle;
use wayland_protocols::wp::linux_dmabuf::zv1::client::zwp_linux_dmabuf_v1::ZwpLinuxDmabufV1;

use crate::cef_app;
use crate::dmabuf::{self, Bo, Feedback, Gbm};
use crate::ffi::*;
use crate::log::{error, info, warning};

/// Our copy targets: current frame on screen, one queued, one being written, one spare.
const MAX_SLOTS: usize = 4;
/// Imports of CEF's buffers kept for reuse (its pool is a few buffers).
const MAX_SOURCES: usize = 6;

// ---------------------------------------------------------------------------------------------
// Buffer pool shared with the Wayland thread
// ---------------------------------------------------------------------------------------------

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum SlotState {
    Free,
    Writing,
    Ready,
    Attached,
}

pub struct Slot {
    id: u64,
    bo: Bo,
    state: SlotState,
    wl_buffer: Option<WlBuffer>,
}

#[derive(Default)]
pub struct Pool {
    size: (i32, i32),
    slots: Vec<Slot>,
    next_id: u64,
    /// Changed area since the last commit (CEF copies whole frames, so this is what the
    /// compositor needs to repaint).
    damage: Option<Rect>,
    /// A frame was skipped because every slot was busy; repaint once one is released.
    dropped: bool,
}

pub struct Shared {
    pool: Mutex<Pool>,
}

static SHARED: OnceLock<Arc<Shared>> = OnceLock::new();
/// Frames attached to the surface (for the log).
static PRESENTED: AtomicU64 = AtomicU64::new(0);
static GPU: Mutex<Option<OverlayGpu>> = Mutex::new(None);

/// Number of overlay frames attached so far.
pub fn presented() -> u64 {
    PRESENTED.load(Ordering::Relaxed)
}

/// The GPU overlay path was set up (decides `shared_texture_enabled` for the browser).
pub fn enabled() -> bool {
    SHARED.get().is_some()
}

/// Attaches the newest finished frame to `surface`, if there is one. Wayland thread. Returns
/// whether something was committed (the caller asked for a frame callback before).
pub fn present<D>(surface: &WlSurface, dmabuf: &ZwpLinuxDmabufV1, qh: &QueueHandle<D>) -> bool
where
    D: wayland_client::Dispatch<wayland_protocols::wp::linux_dmabuf::zv1::client::zwp_linux_buffer_params_v1::ZwpLinuxBufferParamsV1, ()>
        + wayland_client::Dispatch<WlBuffer, dmabuf::BufferData>
        + 'static,
{
    let Some(shared) = SHARED.get() else { return false };
    let mut pool = shared.pool.lock().unwrap();
    let Some(index) = pool.slots.iter().position(|s| s.state == SlotState::Ready) else {
        return false;
    };
    let (w, h) = pool.size;
    let damage = pool.damage.take().unwrap_or(Rect { x: 0, y: 0, width: w, height: h });
    let slot = &mut pool.slots[index];
    if slot.wl_buffer.is_none() {
        let (shared, id) = (shared.clone(), slot.id);
        slot.wl_buffer = Some(dmabuf::create_wl_buffer(dmabuf, qh, &slot.bo, move || released(&shared, id)));
    }
    slot.state = SlotState::Attached;
    let first = PRESENTED.fetch_add(1, Ordering::Relaxed) == 0;
    if first {
        info!("Overlay: first frame presented ({w}x{h}, {} buffer(s))", pool.slots.len());
    }
    let slot = &mut pool.slots[index];
    surface.attach(slot.wl_buffer.as_ref(), 0, 0);
    surface.damage_buffer(damage.x, damage.y, damage.width, damage.height);
    true
}

fn released(shared: &Shared, id: u64) {
    let repaint = {
        let mut pool = shared.pool.lock().unwrap();
        if let Some(slot) = pool.slots.iter_mut().find(|s| s.id == id)
            && slot.state == SlotState::Attached
        {
            slot.state = SlotState::Free;
        }
        std::mem::take(&mut pool.dropped)
    };
    if repaint {
        cef_app::with_host(|host| host.invalidate(cef::PaintElementType::VIEW));
    }
}

// ---------------------------------------------------------------------------------------------
// GPU copy (CEF UI thread)
// ---------------------------------------------------------------------------------------------

struct Gl {
    gen_textures: GlGenTextures,
    delete_textures: GlDeleteTextures,
    bind_texture: GlBindTexture,
    image_target_texture: GlEglImageTargetTexture2dOes,
    gen_framebuffers: GlGenFramebuffers,
    delete_framebuffers: GlDeleteFramebuffers,
    bind_framebuffer: GlBindFramebuffer,
    framebuffer_texture: GlFramebufferTexture2d,
    check_framebuffer_status: GlCheckFramebufferStatus,
    blit_framebuffer: GlBlitFramebuffer,
    finish: GlFinish,
    get_error: GlGetError,
}

impl Gl {
    fn load() -> Result<Gl, String> {
        unsafe fn get<T>(name: &CStr) -> Result<T, String> {
            // SAFETY: T is the matching function pointer type; null is checked.
            unsafe {
                let ptr = eglGetProcAddress(name.as_ptr());
                if ptr.is_null() {
                    return Err(format!("missing GL function {name:?}"));
                }
                Ok(std::mem::transmute_copy(&ptr))
            }
        }
        // SAFETY: names and types match GLES 3 / OES_EGL_image.
        unsafe {
            Ok(Gl {
                gen_textures: get(c"glGenTextures")?,
                delete_textures: get(c"glDeleteTextures")?,
                bind_texture: get(c"glBindTexture")?,
                image_target_texture: get(c"glEGLImageTargetTexture2DOES")?,
                gen_framebuffers: get(c"glGenFramebuffers")?,
                delete_framebuffers: get(c"glDeleteFramebuffers")?,
                bind_framebuffer: get(c"glBindFramebuffer")?,
                framebuffer_texture: get(c"glFramebufferTexture2D")?,
                check_framebuffer_status: get(c"glCheckFramebufferStatus")?,
                blit_framebuffer: get(c"glBlitFramebuffer")?,
                finish: get(c"glFinish")?,
                get_error: get(c"glGetError")?,
            })
        }
    }
}

/// Identity of an imported DMA-BUF (the inode is per buffer, unlike the fd number).
#[derive(Clone, Copy, PartialEq, Eq)]
struct BufferKey {
    dev: u64,
    ino: u64,
    width: i32,
    height: i32,
    format: u32,
    modifier: u64,
    stride: u32,
    offset: u64,
}

/// A DMA-BUF imported as EGLImage -> texture -> framebuffer.
struct Import {
    key: BufferKey,
    image: EGLImage,
    texture: u32,
    framebuffer: u32,
    last_use: u64,
}

struct OverlayGpu {
    gbm: Arc<Gbm>,
    format: u32,
    modifiers: Vec<u64>,
    display: EGLDisplay,
    context: EGLContext,
    gl: Gl,
    current: bool,
    sources: Vec<Import>,
    /// Our slots, by slot id.
    targets: Vec<(u64, Import)>,
    uses: u64,
}

// SAFETY: only used under the GPU mutex, and its GL objects only on the CEF UI thread (the
// context is made current there and nowhere else).
unsafe impl Send for OverlayGpu {}

/// Sets up the GPU overlay path on the compositor's GPU. Wayland thread, before CEF starts.
pub fn init(feedback: &Feedback) -> Result<(), String> {
    let node = dmabuf::render_node(feedback.main_device).ok_or("compositor GPU has no render node")?;
    let gbm = Gbm::open(&node)?;
    let candidates = vec![(DRM_FORMAT_ARGB8888, feedback.modifiers(DRM_FORMAT_ARGB8888))];
    let (format, modifiers) =
        dmabuf::first_allocatable(&gbm, &candidates).ok_or("cannot allocate ARGB8888 buffers the compositor accepts")?;

    // SAFETY: EGL calls on objects created here; failures are checked.
    let (display, context) = unsafe {
        let display = eglGetPlatformDisplay(EGL_PLATFORM_GBM_KHR, gbm.raw(), std::ptr::null());
        if display.is_null() || eglInitialize(display, std::ptr::null_mut(), std::ptr::null_mut()) == EGL_FALSE {
            return Err(format!("EGL (GBM platform) initialisation failed (0x{:x})", eglGetError()));
        }
        let extensions = CStr::from_ptr(eglQueryString(display, EGL_EXTENSIONS)).to_string_lossy().into_owned();
        for required in ["EGL_EXT_image_dma_buf_import_modifiers", "EGL_KHR_surfaceless_context", "EGL_KHR_no_config_context"] {
            if !extensions.split(' ').any(|e| e == required) {
                eglTerminate(display);
                return Err(format!("EGL lacks {required}"));
            }
        }
        eglBindAPI(EGL_OPENGL_ES_API);
        let attribs = [EGL_CONTEXT_MAJOR_VERSION, 3, EGL_NONE];
        let context = eglCreateContext(display, std::ptr::null_mut(), std::ptr::null_mut(), attribs.as_ptr());
        if context.is_null() {
            eglTerminate(display);
            return Err(format!("eglCreateContext(GLES 3) failed (0x{:x})", eglGetError()));
        }
        (display, context)
    };
    let gl = Gl::load()?;

    info!(
        "Overlay: GPU copy on {}, {} modifier(s) for {}",
        node.to_string_lossy(),
        modifiers.len(),
        dmabuf::describe(format, modifiers[0])
    );
    *GPU.lock().unwrap() = Some(OverlayGpu {
        gbm,
        format,
        modifiers,
        display,
        context,
        gl,
        current: false,
        sources: Vec::new(),
        targets: Vec::new(),
        uses: 0,
    });
    let _ = SHARED.set(Arc::new(Shared { pool: Mutex::new(Pool::default()) }));
    Ok(())
}

/// OnAcceleratedPaint: copy CEF's frame into a free slot. Returns whether a frame became Ready.
pub fn paint(info: &AcceleratedPaintInfo, dirty_rects: &[Rect]) -> bool {
    let Some(shared) = SHARED.get() else { return false };
    let mut guard = GPU.lock().unwrap();
    let Some(gpu) = guard.as_mut() else { return false };

    let (width, height) = (info.extra.coded_size.width, info.extra.coded_size.height);
    let format = match info.format.get_raw() {
        1 => DRM_FORMAT_ARGB8888, // CEF_COLOR_TYPE_BGRA_8888
        0 => DRM_FORMAT_ABGR8888, // CEF_COLOR_TYPE_RGBA_8888
        other => {
            warning!("Unsupported overlay frame format {other}");
            return false;
        }
    };
    if info.plane_count != 1 || width <= 0 || height <= 0 {
        warning!("Unsupported overlay frame ({} planes, {width}x{height})", info.plane_count);
        return false;
    }
    if !gpu.make_current() {
        return false;
    }

    // Pick the slot to write.
    let full = Rect { x: 0, y: 0, width, height };
    let (slot_id, target) = {
        let mut pool = shared.pool.lock().unwrap();
        if pool.size != (width, height) {
            for slot in pool.slots.drain(..) {
                if let Some(buffer) = slot.wl_buffer {
                    buffer.destroy();
                }
            }
            gpu.drop_targets();
            pool.size = (width, height);
            pool.damage = Some(full.clone());
        }
        let index = match pool.slots.iter().position(|s| s.state == SlotState::Ready) {
            Some(i) => Some(i), // not attached yet: replace it with this newer frame
            None => pool.slots.iter().position(|s| s.state == SlotState::Free),
        };
        let index = match index {
            Some(i) => i,
            None if pool.slots.len() < MAX_SLOTS => match gpu.gbm.alloc(width, height, gpu.format, &gpu.modifiers) {
                Some(bo) => {
                    let id = pool.next_id;
                    pool.next_id += 1;
                    pool.slots.push(Slot { id, bo, state: SlotState::Free, wl_buffer: None });
                    pool.slots.len() - 1
                }
                None => {
                    error!("Cannot allocate a {width}x{height} overlay buffer");
                    return false;
                }
            },
            None => {
                pool.dropped = true;
                return false;
            }
        };
        let slot = &mut pool.slots[index];
        slot.state = SlotState::Writing;
        let bo = &slot.bo;
        let key = BufferKey {
            dev: 0,
            ino: slot.id,
            width: bo.width,
            height: bo.height,
            format: bo.format,
            modifier: bo.modifier,
            stride: bo.stride,
            offset: bo.offset as u64,
        };
        (slot.id, (key, bo.raw_fd()))
    };

    // SAFETY: CEF's fd is valid during this callback; ours stays open while the slot exists
    // (only this thread removes slots).
    let ok = unsafe { gpu.copy(info, format, width, height, slot_id, target) };

    let mut pool = shared.pool.lock().unwrap();
    let Some(slot) = pool.slots.iter_mut().find(|s| s.id == slot_id) else { return false };
    slot.state = if ok { SlotState::Ready } else { SlotState::Free };
    if ok {
        for r in dirty_rects {
            pool.damage = Some(cef_app::union(pool.damage.clone(), r.clone()));
        }
        if dirty_rects.is_empty() {
            pool.damage = Some(full);
        }
    }
    ok
}

impl OverlayGpu {
    fn make_current(&mut self) -> bool {
        if self.current {
            return true;
        }
        // SAFETY: our display/context; surfaceless (EGL_KHR_surfaceless_context checked).
        if unsafe { eglMakeCurrent(self.display, std::ptr::null_mut(), std::ptr::null_mut(), self.context) } == EGL_FALSE {
            // SAFETY: plain error query.
            error!("Overlay: eglMakeCurrent failed (0x{:x})", unsafe { eglGetError() });
            return false;
        }
        self.current = true;
        true
    }

    fn drop_targets(&mut self) {
        for (_, import) in std::mem::take(&mut self.targets) {
            self.free_import(import);
        }
    }

    fn free_import(&self, import: Import) {
        // SAFETY: GL objects of our (current) context.
        unsafe {
            (self.gl.delete_framebuffers)(1, &import.framebuffer);
            (self.gl.delete_textures)(1, &import.texture);
            eglDestroyImage(self.display, import.image);
        }
    }

    unsafe fn import(&self, key: BufferKey, fd: i32) -> Option<Import> {
        let attribs = [
            EGL_WIDTH,
            key.width as EGLAttrib,
            EGL_HEIGHT,
            key.height as EGLAttrib,
            EGL_LINUX_DRM_FOURCC_EXT,
            key.format as EGLAttrib,
            EGL_DMA_BUF_PLANE0_FD_EXT,
            fd as EGLAttrib,
            EGL_DMA_BUF_PLANE0_OFFSET_EXT,
            key.offset as EGLAttrib,
            EGL_DMA_BUF_PLANE0_PITCH_EXT,
            key.stride as EGLAttrib,
            EGL_DMA_BUF_PLANE0_MODIFIER_LO_EXT,
            (key.modifier & 0xffff_ffff) as EGLAttrib,
            EGL_DMA_BUF_PLANE0_MODIFIER_HI_EXT,
            (key.modifier >> 32) as EGLAttrib,
            EGL_ATTRIB_NONE,
        ];
        // SAFETY: valid attribute list; EGL dups the fd.
        unsafe {
            let image = eglCreateImage(self.display, std::ptr::null_mut(), EGL_LINUX_DMA_BUF_EXT, std::ptr::null_mut(), attribs.as_ptr());
            if image.is_null() {
                error!(
                    "Overlay: cannot import {}x{} {} (0x{:x})",
                    key.width,
                    key.height,
                    dmabuf::describe(key.format, key.modifier),
                    eglGetError()
                );
                return None;
            }
            let gl = &self.gl;
            let (mut texture, mut framebuffer) = (0, 0);
            (gl.gen_textures)(1, &mut texture);
            (gl.bind_texture)(GL_TEXTURE_2D, texture);
            (gl.image_target_texture)(GL_TEXTURE_2D, image);
            (gl.gen_framebuffers)(1, &mut framebuffer);
            (gl.bind_framebuffer)(GL_FRAMEBUFFER, framebuffer);
            (gl.framebuffer_texture)(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, texture, 0);
            let status = (gl.check_framebuffer_status)(GL_FRAMEBUFFER);
            (gl.bind_framebuffer)(GL_FRAMEBUFFER, 0);
            let import = Import { key, image, texture, framebuffer, last_use: 0 };
            if status != GL_FRAMEBUFFER_COMPLETE {
                error!("Overlay: imported buffer is not a complete framebuffer (0x{status:x})");
                self.free_import(import);
                return None;
            }
            Some(import)
        }
    }

    /// Blits CEF's frame into our slot and waits for the GPU (CEF reuses its buffer, and the
    /// compositor reads ours, as soon as we return).
    unsafe fn copy(
        &mut self,
        info: &AcceleratedPaintInfo,
        format: u32,
        width: i32,
        height: i32,
        slot_id: u64,
        (target_key, target_fd): (BufferKey, i32),
    ) -> bool {
        self.uses += 1;
        let plane = &info.planes[0];
        // SAFETY: CEF's fd is valid during the callback.
        let mut st: libc::stat = unsafe { std::mem::zeroed() };
        if unsafe { libc::fstat(plane.fd, &mut st) } != 0 {
            return false;
        }
        let source_key = BufferKey {
            dev: st.st_dev,
            ino: st.st_ino,
            width,
            height,
            format,
            modifier: info.modifier,
            stride: plane.stride,
            offset: plane.offset,
        };

        let source = match self.sources.iter().position(|i| i.key == source_key) {
            Some(i) => i,
            None => {
                // SAFETY: see above.
                let Some(import) = (unsafe { self.import(source_key, plane.fd) }) else { return false };
                if self.sources.len() >= MAX_SOURCES {
                    let oldest = (0..self.sources.len()).min_by_key(|&i| self.sources[i].last_use).unwrap();
                    let old = self.sources.swap_remove(oldest);
                    self.free_import(old);
                }
                self.sources.push(import);
                self.sources.len() - 1
            }
        };
        self.sources[source].last_use = self.uses;
        let source_fb = self.sources[source].framebuffer;

        let target_fb = match self.targets.iter().find(|(id, _)| *id == slot_id) {
            Some((_, import)) => import.framebuffer,
            None => {
                // SAFETY: the slot's fd is open (see paint()).
                let Some(import) = (unsafe { self.import(target_key, target_fd) }) else { return false };
                let fb = import.framebuffer;
                self.targets.push((slot_id, import));
                fb
            }
        };

        let gl = &self.gl;
        // SAFETY: framebuffers of the current context.
        unsafe {
            (gl.bind_framebuffer)(GL_READ_FRAMEBUFFER, source_fb);
            (gl.bind_framebuffer)(GL_DRAW_FRAMEBUFFER, target_fb);
            let (w, h) = (width.min(target_key.width), height.min(target_key.height));
            (gl.blit_framebuffer)(0, 0, w, h, 0, 0, w, h, GL_COLOR_BUFFER_BIT, GL_NEAREST);
            (gl.bind_framebuffer)(GL_FRAMEBUFFER, 0);
            (gl.finish)();
            let err = (gl.get_error)();
            if err != 0 {
                error!("Overlay: GL error 0x{err:x} while copying a frame");
                return false;
            }
        }
        true
    }
}
