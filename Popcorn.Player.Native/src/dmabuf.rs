//! DMA-BUF plumbing shared by the overlay and the video layer: the compositor's linux-dmabuf
//! feedback (which formats/modifiers it can import, on which GPU), GBM buffer allocation on that
//! GPU, and `wl_buffer`s for those buffers with a release callback.
//!
//! The `Dispatch` impls live in window.rs (they need `App`); they forward to the handlers here.
//! `wl_buffer`s are created on the Wayland thread's queue from any thread, so their release
//! events are dispatched there and run the callback stored in [`BufferData`].

use std::collections::BTreeSet;
use std::ffi::{CStr, CString};
use std::os::fd::{AsFd, AsRawFd, FromRawFd, OwnedFd, RawFd};
use std::sync::{Arc, Mutex};

use wayland_client::protocol::wl_buffer::WlBuffer;
use wayland_client::{Dispatch, QueueHandle};
use wayland_protocols::wp::linux_dmabuf::zv1::client::{
    zwp_linux_buffer_params_v1::{self, ZwpLinuxBufferParamsV1},
    zwp_linux_dmabuf_feedback_v1,
    zwp_linux_dmabuf_v1::ZwpLinuxDmabufV1,
};

use crate::ffi::*;
use crate::log::{info, warning};

// ---------------------------------------------------------------------------------------------
// Compositor feedback
// ---------------------------------------------------------------------------------------------

/// What the compositor can import: formats + modifiers of the tranches for its main device.
#[derive(Clone, Debug, Default)]
pub struct Feedback {
    pub main_device: libc::dev_t,
    pub formats: BTreeSet<(u32, u64)>,
}

impl Feedback {
    pub fn modifiers(&self, format: u32) -> Vec<u64> {
        self.formats
            .iter()
            .filter(|(f, m)| *f == format && *m != DRM_FORMAT_MOD_INVALID)
            .map(|(_, m)| *m)
            .collect()
    }
}

/// User data of the default `zwp_linux_dmabuf_feedback_v1`: the feedback being assembled, and
/// the last complete one.
#[derive(Default)]
pub struct FeedbackData {
    state: Mutex<FeedbackBuilder>,
}

#[derive(Default)]
struct FeedbackBuilder {
    table: Vec<(u32, u64)>,
    main_device: libc::dev_t,
    tranche_device: libc::dev_t,
    tranche_formats: Vec<u16>,
    formats: BTreeSet<(u32, u64)>,
    done: Option<Feedback>,
}

impl FeedbackData {
    pub fn result(&self) -> Option<Feedback> {
        self.state.lock().unwrap().done.clone()
    }

    pub fn handle(&self, event: zwp_linux_dmabuf_feedback_v1::Event) {
        use zwp_linux_dmabuf_feedback_v1::Event;
        let mut s = self.state.lock().unwrap();
        match event {
            Event::FormatTable { fd, size } => s.table = read_format_table(fd, size as usize),
            Event::MainDevice { device } => s.main_device = dev_from_bytes(&device),
            Event::TrancheTargetDevice { device } => s.tranche_device = dev_from_bytes(&device),
            Event::TrancheFormats { indices } => {
                s.tranche_formats = indices.chunks_exact(2).map(|c| u16::from_ne_bytes([c[0], c[1]])).collect();
            }
            Event::TrancheFlags { .. } => {}
            Event::TrancheDone => {
                // Tranches for other devices (e.g. a scan-out-only GPU) aren't importable by ours.
                if s.tranche_device == s.main_device {
                    let indices = std::mem::take(&mut s.tranche_formats);
                    for i in indices {
                        if let Some(&entry) = s.table.get(i as usize) {
                            s.formats.insert(entry);
                        }
                    }
                }
                s.tranche_formats.clear();
            }
            Event::Done => {
                let feedback = Feedback {
                    main_device: s.main_device,
                    formats: std::mem::take(&mut s.formats),
                };
                s.done = Some(feedback);
            }
            _ => {}
        }
    }
}

fn dev_from_bytes(bytes: &[u8]) -> libc::dev_t {
    let mut buf = [0u8; std::mem::size_of::<libc::dev_t>()];
    let n = bytes.len().min(buf.len());
    buf[..n].copy_from_slice(&bytes[..n]);
    libc::dev_t::from_ne_bytes(buf)
}

fn read_format_table(fd: OwnedFd, size: usize) -> Vec<(u32, u64)> {
    if size == 0 {
        return Vec::new();
    }
    // SAFETY: mapping a read-only table the compositor sized for us; unmapped before returning.
    unsafe {
        let ptr = libc::mmap(std::ptr::null_mut(), size, libc::PROT_READ, libc::MAP_PRIVATE, fd.as_raw_fd(), 0);
        if ptr == libc::MAP_FAILED {
            warning!("Cannot map the dmabuf format table");
            return Vec::new();
        }
        // Entries: u32 format, u32 padding, u64 modifier.
        let bytes = std::slice::from_raw_parts(ptr as *const u8, size);
        let table = bytes
            .chunks_exact(16)
            .map(|e| {
                let format = u32::from_ne_bytes(e[0..4].try_into().unwrap());
                let modifier = u64::from_ne_bytes(e[8..16].try_into().unwrap());
                (format, modifier)
            })
            .collect();
        libc::munmap(ptr, size);
        table
    }
}

/// Render node (e.g. /dev/dri/renderD128) of the DRM device `dev` (primary or render node).
pub fn render_node(dev: libc::dev_t) -> Option<CString> {
    // SAFETY: libdrm allocates the device description; freed below.
    unsafe {
        let mut device: *mut drmDevice = std::ptr::null_mut();
        if drmGetDeviceFromDevId(dev, 0, &mut device) != 0 || device.is_null() {
            return None;
        }
        let d = &*device;
        let path = (d.available_nodes & (1 << DRM_NODE_RENDER) != 0)
            .then(|| CStr::from_ptr(*d.nodes.add(DRM_NODE_RENDER)).to_owned());
        drmFreeDevice(&mut device);
        path
    }
}

// ---------------------------------------------------------------------------------------------
// GBM
// ---------------------------------------------------------------------------------------------

pub struct Gbm {
    device: *mut gbm_device,
    _fd: OwnedFd,
}

// SAFETY: each Gbm is used by one thread at a time (the owner allocates; buffers are only
// destroyed, which libgbm allows from any thread).
unsafe impl Send for Gbm {}
unsafe impl Sync for Gbm {}

impl Gbm {
    pub fn open(path: &CStr) -> Result<Arc<Gbm>, String> {
        // SAFETY: plain open(2); the fd is owned below.
        let fd = unsafe { libc::open(path.as_ptr(), libc::O_RDWR | libc::O_CLOEXEC) };
        if fd < 0 {
            return Err(format!("Cannot open {path:?}: {}", std::io::Error::last_os_error()));
        }
        // SAFETY: fd is a fresh, valid descriptor.
        let fd = unsafe { OwnedFd::from_raw_fd(fd) };
        // SAFETY: valid DRM fd; it outlives the device (dropped after it).
        let device = unsafe { gbm_create_device(fd.as_raw_fd()) };
        if device.is_null() {
            return Err(format!("gbm_create_device({path:?}) failed"));
        }
        Ok(Arc::new(Gbm { device, _fd: fd }))
    }

    pub fn raw(&self) -> *mut gbm_device {
        self.device
    }

    /// A single-plane buffer usable as a GPU render target, with one of `modifiers`.
    pub fn alloc(self: &Arc<Gbm>, width: i32, height: i32, format: u32, modifiers: &[u64]) -> Option<Bo> {
        if modifiers.is_empty() || width <= 0 || height <= 0 {
            return None;
        }
        // SAFETY: valid device; the modifier list is read during the call only.
        unsafe {
            let bo = gbm_bo_create_with_modifiers2(
                self.device,
                width as u32,
                height as u32,
                format,
                modifiers.as_ptr(),
                modifiers.len() as u32,
                GBM_BO_USE_RENDERING,
            );
            if bo.is_null() {
                return None;
            }
            let fd = gbm_bo_get_fd_for_plane(bo, 0);
            if gbm_bo_get_plane_count(bo) != 1 || fd < 0 {
                if fd >= 0 {
                    libc::close(fd);
                }
                gbm_bo_destroy(bo);
                return None;
            }
            Some(Bo {
                bo,
                fd: OwnedFd::from_raw_fd(fd),
                width,
                height,
                format,
                modifier: gbm_bo_get_modifier(bo),
                stride: gbm_bo_get_stride_for_plane(bo, 0),
                offset: gbm_bo_get_offset(bo, 0),
                _gbm: self.clone(),
            })
        }
    }
}

impl Drop for Gbm {
    fn drop(&mut self) {
        // SAFETY: all Bos hold an Arc to us, so none is left.
        unsafe { gbm_device_destroy(self.device) };
    }
}

/// A GBM buffer object and its DMA-BUF.
pub struct Bo {
    bo: *mut gbm_bo,
    pub fd: OwnedFd,
    pub width: i32,
    pub height: i32,
    pub format: u32,
    pub modifier: u64,
    pub stride: u32,
    pub offset: u32,
    _gbm: Arc<Gbm>,
}

// SAFETY: see Gbm.
unsafe impl Send for Bo {}
unsafe impl Sync for Bo {}

impl Bo {
    pub fn raw_fd(&self) -> RawFd {
        self.fd.as_raw_fd()
    }
}

impl Drop for Bo {
    fn drop(&mut self) {
        // SAFETY: we own the bo; consumers that imported the DMA-BUF hold their own reference.
        unsafe { gbm_bo_destroy(self.bo) };
    }
}

/// Picks the first of `candidates` (format + modifiers, in order of preference) GBM can
/// allocate, by allocating a test buffer.
pub fn first_allocatable(gbm: &Arc<Gbm>, candidates: &[(u32, Vec<u64>)]) -> Option<(u32, Vec<u64>)> {
    candidates
        .iter()
        .find(|(format, modifiers)| gbm.alloc(64, 64, *format, modifiers).is_some())
        .cloned()
}

// ---------------------------------------------------------------------------------------------
// wl_buffer
// ---------------------------------------------------------------------------------------------

/// User data of our dmabuf `wl_buffer`s.
pub struct BufferData {
    on_release: Box<dyn Fn() + Send + Sync>,
}

impl BufferData {
    pub fn released(&self) {
        (self.on_release)();
    }
}

/// Wraps `bo` in a `wl_buffer` (the compositor imports it on the first attach). `on_release`
/// runs on the Wayland thread when the compositor is done reading a commit of it.
pub fn create_wl_buffer<D>(
    dmabuf: &ZwpLinuxDmabufV1,
    qh: &QueueHandle<D>,
    bo: &Bo,
    on_release: impl Fn() + Send + Sync + 'static,
) -> WlBuffer
where
    D: Dispatch<ZwpLinuxBufferParamsV1, ()> + Dispatch<WlBuffer, BufferData> + 'static,
{
    let params = dmabuf.create_params(qh, ());
    params.add(
        bo.fd.as_fd(),
        0,
        bo.offset,
        bo.stride,
        (bo.modifier >> 32) as u32,
        (bo.modifier & 0xffff_ffff) as u32,
    );
    let buffer = params.create_immed(
        bo.width,
        bo.height,
        bo.format,
        zwp_linux_buffer_params_v1::Flags::empty(),
        qh,
        BufferData {
            on_release: Box::new(on_release),
        },
    );
    params.destroy();
    buffer
}

pub fn no_op_buffer_data() -> BufferData {
    BufferData {
        on_release: Box::new(|| {}),
    }
}

pub fn describe(format: u32, modifier: u64) -> String {
    let code: String = format.to_le_bytes().iter().map(|&b| b as char).collect();
    format!("{code} 0x{modifier:x}")
}

pub fn log_feedback(feedback: &Feedback) {
    info!(
        "Compositor dmabuf device {}:{}, {} format/modifier pairs",
        libc::major(feedback.main_device),
        libc::minor(feedback.main_device),
        feedback.formats.len()
    );
}
