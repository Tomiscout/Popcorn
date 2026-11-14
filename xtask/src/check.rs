//! `check`: everything a change should pass before it is committed, on either OS. `test`: the tests alone.

use anyhow::Result;

use crate::util::{cmd, path, run, step};
use crate::web;

/// Every .NET entry project (the rest are built as their references). The Windows ones also compile on Linux
/// (EnableWindowsTargeting; Popcorn.Player.Windows builds its Rust layer only on Windows).
const DOTNET_PROJECTS: &[&str] = &[
    "Popcorn.Server",
    "Popcorn.Player.Linux",
    "Popcorn.Player.Windows",
    "Popcorn.Windows",
    "Popcorn.Core.Tests",
    "Popcorn.FilenameParser.Tests",
];
const DOTNET_TESTS: &[&str] = &["Popcorn.Core.Tests", "Popcorn.FilenameParser.Tests"];

pub fn check() -> Result<()> {
    // First: the server build writes the socket contract the web UI is type-checked against
    for project in DOTNET_PROJECTS {
        step(&format!("{project} (build)"));
        run(cmd("dotnet").arg("build").arg(path(project)))?;
    }
    dotnet_tests(true)?;

    step("Popcorn.Web (type check, lint)");
    let dir = path("Popcorn.Web");
    web::deps(&dir)?;
    run(cmd("npx").args(["tsc", "-b"]).current_dir(&dir))?;
    run(cmd("npm").args(["run", "lint"]).current_dir(&dir))?;

    step("Rust (clippy, tests)");
    // Warnings are reported, not fatal: some only show up on one OS
    run(cmd("cargo").args(["clippy", "--workspace", "--all-targets"]))?;
    run(cmd("cargo").args(["test", "--workspace"]))?;
    println!("\nAll checks passed");
    Ok(())
}

pub fn test() -> Result<()> {
    dotnet_tests(false)?;
    step("Rust tests");
    run(cmd("cargo").args(["test", "--workspace"]))
}

fn dotnet_tests(built: bool) -> Result<()> {
    for project in DOTNET_TESTS {
        step(&format!("{project} (test)"));
        let mut test = cmd("dotnet");
        test.arg("test").arg(path(project));
        if built {
            test.arg("--no-build");
        }
        run(&mut test)?;
    }
    Ok(())
}
