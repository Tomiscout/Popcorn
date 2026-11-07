//! CEF helper process (renderer, GPU, utility, ...) for the Linux player.
//!
//! The browser process lives inside Popcorn.Player.Linux (.NET + libpopcorn_player.so); CEF starts
//! this executable for everything else via `browser_subprocess_path`.

use cef::{api_hash, args::Args, execute_process, sys};

fn main() {
    let _ = api_hash(sys::CEF_API_VERSION_LAST, 0);
    let args = Args::new();
    let code = execute_process(Some(args.as_main_args()), None, std::ptr::null_mut());
    // execute_process returns -1 for the browser process, which this binary never is.
    std::process::exit(code.max(0));
}
