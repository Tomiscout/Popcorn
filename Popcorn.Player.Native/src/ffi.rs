//! Hand-written bindings for the few C entry points we need from libmpv, libEGL,
//! libwayland-egl, libgbm and libdrm (linked in build.rs). Kept minimal on purpose; see the
//! system headers (`mpv/render*.h`, `EGL/egl*.h`, `GLES3/gl3.h`, `wayland-egl-core.h`, `gbm.h`,
//! `xf86drm.h`, `drm_fourcc.h`).

#![allow(non_camel_case_types, dead_code)]

use std::ffi::{c_char, c_int, c_void};

// ---------------------------------------------------------------------------------------------
// libmpv
// ---------------------------------------------------------------------------------------------

pub type mpv_handle = c_void;
pub type mpv_render_context = c_void;

#[repr(C)]
pub struct mpv_render_param {
    pub type_: c_int,
    pub data: *mut c_void,
}

pub const MPV_RENDER_PARAM_INVALID: c_int = 0;
pub const MPV_RENDER_PARAM_API_TYPE: c_int = 1;
pub const MPV_RENDER_PARAM_OPENGL_INIT_PARAMS: c_int = 2;
pub const MPV_RENDER_PARAM_OPENGL_FBO: c_int = 3;
pub const MPV_RENDER_PARAM_FLIP_Y: c_int = 4;
pub const MPV_RENDER_PARAM_WL_DISPLAY: c_int = 9;
// render_dmabuf.h (mpv with the DMA-BUF render backend, client API >= 2.7).
pub const MPV_RENDER_PARAM_DMABUF_INIT_PARAMS: c_int = 23;
pub const MPV_RENDER_PARAM_DMABUF_FBO: c_int = 24;
pub const MPV_RENDER_PARAM_DMABUF_FORMATS: c_int = 25;

pub const MPV_ERROR_NOT_IMPLEMENTED: c_int = -19;

pub const MPV_RENDER_UPDATE_FRAME: u64 = 1;

#[repr(C)]
pub struct mpv_opengl_init_params {
    pub get_proc_address: Option<unsafe extern "C" fn(ctx: *mut c_void, name: *const c_char) -> *mut c_void>,
    pub get_proc_address_ctx: *mut c_void,
}

#[repr(C)]
pub struct mpv_opengl_fbo {
    pub fbo: c_int,
    pub w: c_int,
    pub h: c_int,
    pub internal_format: c_int,
}

#[repr(C)]
pub struct mpv_dmabuf_init_params {
    pub drm_device: *const c_char,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct mpv_dmabuf_plane {
    pub fd: c_int,
    pub offset: u32,
    pub stride: u32,
}

#[repr(C)]
pub struct mpv_dmabuf_fbo {
    pub w: c_int,
    pub h: c_int,
    pub format: u32,
    pub modifier: u64,
    pub num_planes: c_int,
    pub planes: [mpv_dmabuf_plane; 4],
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct mpv_dmabuf_format {
    pub format: u32,
    pub modifier: u64,
}

#[repr(C)]
pub struct mpv_dmabuf_formats {
    pub formats: *const mpv_dmabuf_format,
    pub num_formats: c_int,
}

unsafe extern "C" {
    pub fn mpv_create() -> *mut mpv_handle;
    pub fn mpv_initialize(ctx: *mut mpv_handle) -> c_int;
    pub fn mpv_set_option_string(ctx: *mut mpv_handle, name: *const c_char, data: *const c_char) -> c_int;
    pub fn mpv_terminate_destroy(ctx: *mut mpv_handle);
    pub fn mpv_error_string(error: c_int) -> *const c_char;

    pub fn mpv_render_context_create(
        res: *mut *mut mpv_render_context,
        mpv: *mut mpv_handle,
        params: *mut mpv_render_param,
    ) -> c_int;
    pub fn mpv_render_context_set_update_callback(
        ctx: *mut mpv_render_context,
        callback: Option<unsafe extern "C" fn(cb_ctx: *mut c_void)>,
        callback_ctx: *mut c_void,
    );
    pub fn mpv_render_context_update(ctx: *mut mpv_render_context) -> u64;
    pub fn mpv_render_context_get_info(ctx: *mut mpv_render_context, param: mpv_render_param) -> c_int;
    pub fn mpv_render_context_render(ctx: *mut mpv_render_context, params: *mut mpv_render_param) -> c_int;
    pub fn mpv_render_context_report_swap(ctx: *mut mpv_render_context);
    pub fn mpv_render_context_free(ctx: *mut mpv_render_context);
}

pub fn mpv_error(code: c_int) -> String {
    // SAFETY: mpv_error_string returns a static string for any value.
    unsafe { std::ffi::CStr::from_ptr(mpv_error_string(code)) }
        .to_string_lossy()
        .into_owned()
}

// ---------------------------------------------------------------------------------------------
// EGL
// ---------------------------------------------------------------------------------------------

pub type EGLDisplay = *mut c_void;
pub type EGLConfig = *mut c_void;
pub type EGLContext = *mut c_void;
pub type EGLSurface = *mut c_void;
pub type EGLint = i32;
pub type EGLBoolean = u32;
pub type EGLAttrib = isize;

pub const EGL_FALSE: EGLBoolean = 0;
pub const EGL_NONE: EGLint = 0x3038;
pub const EGL_ALPHA_SIZE: EGLint = 0x3021;
pub const EGL_BLUE_SIZE: EGLint = 0x3022;
pub const EGL_GREEN_SIZE: EGLint = 0x3023;
pub const EGL_RED_SIZE: EGLint = 0x3024;
pub const EGL_SURFACE_TYPE: EGLint = 0x3033;
pub const EGL_RENDERABLE_TYPE: EGLint = 0x3040;
pub const EGL_WINDOW_BIT: EGLint = 0x0004;
pub const EGL_OPENGL_BIT: EGLint = 0x0008;
pub const EGL_OPENGL_API: u32 = 0x30A2;
pub const EGL_PLATFORM_WAYLAND_KHR: u32 = 0x31D8;
pub const EGL_PLATFORM_GBM_KHR: u32 = 0x31D7;
pub const EGL_OPENGL_ES_API: u32 = 0x30A0;
pub const EGL_CONTEXT_MAJOR_VERSION: EGLint = 0x3098;
pub const EGL_EXTENSIONS: EGLint = 0x3055;
pub const EGL_WIDTH: EGLAttrib = 0x3057;
pub const EGL_HEIGHT: EGLAttrib = 0x3056;
pub const EGL_LINUX_DMA_BUF_EXT: u32 = 0x3270;
pub const EGL_LINUX_DRM_FOURCC_EXT: EGLAttrib = 0x3271;
pub const EGL_DMA_BUF_PLANE0_FD_EXT: EGLAttrib = 0x3272;
pub const EGL_DMA_BUF_PLANE0_OFFSET_EXT: EGLAttrib = 0x3273;
pub const EGL_DMA_BUF_PLANE0_PITCH_EXT: EGLAttrib = 0x3274;
pub const EGL_DMA_BUF_PLANE0_MODIFIER_LO_EXT: EGLAttrib = 0x3443;
pub const EGL_DMA_BUF_PLANE0_MODIFIER_HI_EXT: EGLAttrib = 0x3444;
pub const EGL_ATTRIB_NONE: EGLAttrib = 0x3038;

pub type EGLImage = *mut c_void;

unsafe extern "C" {
    pub fn eglGetPlatformDisplay(platform: u32, native_display: *mut c_void, attribs: *const EGLAttrib) -> EGLDisplay;
    pub fn eglInitialize(dpy: EGLDisplay, major: *mut EGLint, minor: *mut EGLint) -> EGLBoolean;
    pub fn eglBindAPI(api: u32) -> EGLBoolean;
    pub fn eglChooseConfig(
        dpy: EGLDisplay,
        attribs: *const EGLint,
        configs: *mut EGLConfig,
        config_size: EGLint,
        num_config: *mut EGLint,
    ) -> EGLBoolean;
    pub fn eglCreateContext(dpy: EGLDisplay, config: EGLConfig, share: EGLContext, attribs: *const EGLint) -> EGLContext;
    pub fn eglCreateWindowSurface(dpy: EGLDisplay, config: EGLConfig, win: *mut c_void, attribs: *const EGLint) -> EGLSurface;
    pub fn eglMakeCurrent(dpy: EGLDisplay, draw: EGLSurface, read: EGLSurface, ctx: EGLContext) -> EGLBoolean;
    pub fn eglSwapInterval(dpy: EGLDisplay, interval: EGLint) -> EGLBoolean;
    pub fn eglSwapBuffers(dpy: EGLDisplay, surface: EGLSurface) -> EGLBoolean;
    pub fn eglGetProcAddress(name: *const c_char) -> *mut c_void;
    pub fn eglDestroySurface(dpy: EGLDisplay, surface: EGLSurface) -> EGLBoolean;
    pub fn eglDestroyContext(dpy: EGLDisplay, ctx: EGLContext) -> EGLBoolean;
    pub fn eglGetError() -> EGLint;
    pub fn eglQueryString(dpy: EGLDisplay, name: EGLint) -> *const c_char;
    pub fn eglCreateImage(dpy: EGLDisplay, ctx: EGLContext, target: u32, buffer: *mut c_void, attribs: *const EGLAttrib) -> EGLImage;
    pub fn eglDestroyImage(dpy: EGLDisplay, image: EGLImage) -> EGLBoolean;
    pub fn eglTerminate(dpy: EGLDisplay) -> EGLBoolean;
}

// ---------------------------------------------------------------------------------------------
// libwayland-egl
// ---------------------------------------------------------------------------------------------

pub type wl_egl_window = c_void;

unsafe extern "C" {
    pub fn wl_egl_window_create(surface: *mut c_void, width: c_int, height: c_int) -> *mut wl_egl_window;
    pub fn wl_egl_window_resize(win: *mut wl_egl_window, width: c_int, height: c_int, dx: c_int, dy: c_int);
    pub fn wl_egl_window_destroy(win: *mut wl_egl_window);
}

// ---------------------------------------------------------------------------------------------
// OpenGL (resolved through eglGetProcAddress)
// ---------------------------------------------------------------------------------------------

pub const GL_COLOR_BUFFER_BIT: u32 = 0x4000;
pub type GlClearColor = unsafe extern "C" fn(r: f32, g: f32, b: f32, a: f32);
pub type GlClear = unsafe extern "C" fn(mask: u32);
pub type GlViewport = unsafe extern "C" fn(x: i32, y: i32, w: i32, h: i32);

pub const GL_TEXTURE_2D: u32 = 0x0DE1;
pub const GL_FRAMEBUFFER: u32 = 0x8D40;
pub const GL_READ_FRAMEBUFFER: u32 = 0x8CA8;
pub const GL_DRAW_FRAMEBUFFER: u32 = 0x8CA9;
pub const GL_COLOR_ATTACHMENT0: u32 = 0x8CE0;
pub const GL_FRAMEBUFFER_COMPLETE: u32 = 0x8CD5;
pub const GL_NEAREST: u32 = 0x2600;
pub type GlGenTextures = unsafe extern "C" fn(n: i32, textures: *mut u32);
pub type GlDeleteTextures = unsafe extern "C" fn(n: i32, textures: *const u32);
pub type GlBindTexture = unsafe extern "C" fn(target: u32, texture: u32);
pub type GlEglImageTargetTexture2dOes = unsafe extern "C" fn(target: u32, image: EGLImage);
pub type GlGenFramebuffers = unsafe extern "C" fn(n: i32, framebuffers: *mut u32);
pub type GlDeleteFramebuffers = unsafe extern "C" fn(n: i32, framebuffers: *const u32);
pub type GlBindFramebuffer = unsafe extern "C" fn(target: u32, framebuffer: u32);
pub type GlFramebufferTexture2d = unsafe extern "C" fn(target: u32, attachment: u32, textarget: u32, texture: u32, level: i32);
pub type GlCheckFramebufferStatus = unsafe extern "C" fn(target: u32) -> u32;
pub type GlBlitFramebuffer =
    unsafe extern "C" fn(sx0: i32, sy0: i32, sx1: i32, sy1: i32, dx0: i32, dy0: i32, dx1: i32, dy1: i32, mask: u32, filter: u32);
pub type GlFinish = unsafe extern "C" fn();
pub type GlGetError = unsafe extern "C" fn() -> u32;

// ---------------------------------------------------------------------------------------------
// libgbm
// ---------------------------------------------------------------------------------------------

pub type gbm_device = c_void;
pub type gbm_bo = c_void;

pub const GBM_BO_USE_RENDERING: u32 = 1 << 2;

unsafe extern "C" {
    pub fn gbm_create_device(fd: c_int) -> *mut gbm_device;
    pub fn gbm_device_destroy(gbm: *mut gbm_device);
    pub fn gbm_bo_create_with_modifiers2(
        gbm: *mut gbm_device,
        width: u32,
        height: u32,
        format: u32,
        modifiers: *const u64,
        count: u32,
        flags: u32,
    ) -> *mut gbm_bo;
    pub fn gbm_bo_destroy(bo: *mut gbm_bo);
    pub fn gbm_bo_get_plane_count(bo: *mut gbm_bo) -> c_int;
    pub fn gbm_bo_get_fd_for_plane(bo: *mut gbm_bo, plane: c_int) -> c_int;
    pub fn gbm_bo_get_stride_for_plane(bo: *mut gbm_bo, plane: c_int) -> u32;
    pub fn gbm_bo_get_offset(bo: *mut gbm_bo, plane: c_int) -> u32;
    pub fn gbm_bo_get_modifier(bo: *mut gbm_bo) -> u64;
}

// ---------------------------------------------------------------------------------------------
// libdrm
// ---------------------------------------------------------------------------------------------

pub const DRM_NODE_RENDER: usize = 2;

/// Leading fields of `drmDevice` (the rest is bus specific and not needed).
#[repr(C)]
pub struct drmDevice {
    pub nodes: *mut *mut c_char,
    pub available_nodes: c_int,
}

unsafe extern "C" {
    pub fn drmGetDeviceFromDevId(dev: libc::dev_t, flags: u32, device: *mut *mut drmDevice) -> c_int;
    pub fn drmFreeDevice(device: *mut *mut drmDevice);
}

// drm_fourcc.h
pub const fn fourcc(code: &[u8; 4]) -> u32 {
    code[0] as u32 | (code[1] as u32) << 8 | (code[2] as u32) << 16 | (code[3] as u32) << 24
}
pub const DRM_FORMAT_ARGB8888: u32 = fourcc(b"AR24");
pub const DRM_FORMAT_XRGB8888: u32 = fourcc(b"XR24");
pub const DRM_FORMAT_ABGR8888: u32 = fourcc(b"AB24");
pub const DRM_FORMAT_MOD_INVALID: u64 = 0x00ff_ffff_ffff_ffff;
