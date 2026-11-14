fn main() {
    // Stub crate on other OSes (see src/), nothing to link.
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("linux") {
        return;
    }
    // libcef.so is shipped next to the executable.
    println!("cargo:rustc-link-arg-bins=-Wl,-rpath,$ORIGIN");
}
