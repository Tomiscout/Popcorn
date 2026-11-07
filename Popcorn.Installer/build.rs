//! Exe icon = the web UI's popcorn.ico (all its sizes), compiled into a Windows resource.

fn main() {
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed=../Popcorn.Web/public/images/popcorn.ico");
    #[cfg(windows)]
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("windows") {
        let mut resource = winresource::WindowsResource::new();
        resource.set_icon("../Popcorn.Web/public/images/popcorn.ico");
        // Without a resource compiler the exe only lacks its icon; the window still sets one at runtime.
        if let Err(e) = resource.compile() {
            println!("cargo:warning=exe icon skipped: {e}");
        }
    }
}
