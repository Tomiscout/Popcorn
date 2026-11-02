fn main() {
    // libcef.so (and the rest of the CEF runtime) is shipped next to the library and the helper binary.
    println!("cargo:rustc-link-arg-cdylib=-Wl,-rpath,$ORIGIN");
    println!("cargo:rustc-link-arg-bins=-Wl,-rpath,$ORIGIN");
    // System libraries used through hand-written FFI (src/ffi.rs).
    println!("cargo:rustc-link-lib=dylib=mpv");
    println!("cargo:rustc-link-lib=dylib=EGL");
    println!("cargo:rustc-link-lib=dylib=wayland-egl");
    println!("cargo:rustc-link-lib=dylib=gbm");
    println!("cargo:rustc-link-lib=dylib=drm");
}
