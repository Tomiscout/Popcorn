//! .NET builds. The apps are published with ReadyToRun (framework-dependent, code precompiled to native): no JIT at
//! start-up, which matters most for the file name parser's source-generated regexes. `build` uses the same
//! configuration, RID and properties as `publish`, so the publish right after it only adds the R2R step.

use std::path::PathBuf;

use anyhow::Result;

use crate::util::{cmd, path, publish_dir, remove_dir, rid, run, step};

fn args(version: &str) -> Vec<String> {
    ["--configuration", "Release", "--runtime", rid(), "--self-contained", "false", "-p:PublishReadyToRun=true"]
        .into_iter()
        .map(String::from)
        .chain([format!("-p:Version={version}")])
        .collect()
}

pub fn build(project: &str, version: &str) -> Result<()> {
    step(&format!("{project} (build)"));
    run(cmd("dotnet").arg("build").arg(path(project)).args(args(version)))
}

/// Publishes into Bundle/Publish/<project> (replaced) and returns that folder.
pub fn publish(project: &str, version: &str) -> Result<PathBuf> {
    step(&format!("{project} (publish)"));
    let out = publish_dir(project);
    remove_dir(&out)?;
    run(cmd("dotnet").arg("publish").arg(path(project)).args(args(version)).arg("--output").arg(&out))?;
    Ok(out)
}
