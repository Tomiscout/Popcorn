fn main() {
    // libcef.so is shipped next to the executable.
    println!("cargo:rustc-link-arg-bins=-Wl,-rpath,$ORIGIN");
}
