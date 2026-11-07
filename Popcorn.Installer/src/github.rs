//! GitHub releases: the newest release and its platform package.
//! Release layout (release_windows.ps1, release_linux.sh): tag `v<version>`, assets `Popcorn-<version>-<platform>.zip`
//! (checked against the SHA-256 digest GitHub computes on upload) + the standalone installers.

use std::fs::File;
use std::io::{Read, Write};
use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;

use anyhow::{Context, bail};
use serde::Deserialize;
use sha2::{Digest, Sha256};

use crate::platform;

/// Built in at compile time with POPCORN_UPDATE_REPO, overridable at runtime by the same variable or `--repo`.
const BUILT_IN_REPO: &str = match option_env!("POPCORN_UPDATE_REPO") {
    Some(repo) => repo,
    None => "Tomiscout/Popcorn",
};
const API: &str = "https://api.github.com";

pub fn default_repo() -> String {
    std::env::var("POPCORN_UPDATE_REPO")
        .ok()
        .filter(|r| !r.is_empty())
        .unwrap_or_else(|| BUILT_IN_REPO.to_string())
}

/// Needed while the repository is private; never built into the program.
pub fn token_from_env() -> Option<String> {
    ["POPCORN_GITHUB_TOKEN", "GITHUB_TOKEN"]
        .iter()
        .find_map(|name| std::env::var(name).ok().filter(|t| !t.trim().is_empty()))
        .map(|t| t.trim().to_string())
}

#[derive(Clone, Debug, Deserialize)]
pub struct Release {
    pub tag_name: String,
    #[serde(default)]
    pub name: Option<String>,
    #[serde(default)]
    pub body: Option<String>,
    #[serde(default)]
    pub draft: bool,
    #[serde(default)]
    pub prerelease: bool,
    #[serde(default)]
    pub assets: Vec<Asset>,
}

impl Release {
    pub fn version(&self) -> &str {
        self.tag_name.trim_start_matches(['v', 'V'])
    }

    /// `...-<platform>.zip`, e.g. `Popcorn-1.2.0-windows-x64.zip`.
    pub fn package(&self) -> Option<&Asset> {
        let suffix = format!("-{}.zip", platform::PLATFORM).to_lowercase();
        self.assets
            .iter()
            .find(|a| a.name.to_lowercase().ends_with(&suffix))
    }
}

#[derive(Clone, Debug, Deserialize)]
pub struct Asset {
    pub name: String,
    /// API URL: works for private repositories too (with `Accept: application/octet-stream`).
    pub url: String,
    pub size: u64,
    /// `sha256:<hex>` (assets uploaded since mid 2025).
    #[serde(default)]
    pub digest: Option<String>,
    #[serde(default)]
    pub updated_at: Option<String>,
}

impl Asset {
    /// The SHA-256 GitHub computed on upload, hex.
    pub fn sha256(&self) -> Option<String> {
        self.digest
            .as_deref()?
            .strip_prefix("sha256:")
            .map(str::to_lowercase)
    }
}

pub struct GitHub {
    agent: ureq::Agent,
    repo: String,
    token: Option<String>,
}

impl GitHub {
    pub fn new(repo: &str, token: Option<String>) -> Self {
        // The OS TLS stack (Windows: SChannel and its certificate store, no C compiler needed to build as ring would;
        // Linux: OpenSSL with the system's certificates).
        let tls = ureq::tls::TlsConfig::builder()
            .provider(ureq::tls::TlsProvider::NativeTls)
            .build();
        let agent = ureq::Agent::config_builder()
            .tls_config(tls)
            .timeout_connect(Some(Duration::from_secs(20)))
            .timeout_recv_response(Some(Duration::from_secs(60)))
            .user_agent(format!("Popcorn.Installer/{}", env!("CARGO_PKG_VERSION")))
            .build()
            .into();
        Self {
            agent,
            repo: repo.to_string(),
            token,
        }
    }

    fn get(&self, url: &str, accept: &str) -> ureq::RequestBuilder<ureq::typestate::WithoutBody> {
        let request = self
            .agent
            .get(url)
            .header("Accept", accept)
            .header("X-GitHub-Api-Version", "2022-11-28");
        // ureq drops it on the redirect to the storage host, which would refuse it.
        match &self.token {
            Some(token) => request.header("Authorization", format!("Bearer {token}")),
            None => request,
        }
    }

    fn call(&self, url: &str, accept: &str) -> anyhow::Result<ureq::http::Response<ureq::Body>> {
        match self.get(url, accept).call() {
            Ok(response) => Ok(response),
            Err(ureq::Error::StatusCode(code @ (403 | 429))) if self.token.is_none() => bail!(
                "GitHub answered {code} for {url} (rate limit of anonymous requests, 60 an hour, \
                 or a private repository: set POPCORN_GITHUB_TOKEN)"
            ),
            Err(ureq::Error::StatusCode(code @ (401 | 403 | 404 | 429))) => {
                let hint = if self.token.is_some() {
                    "check that POPCORN_GITHUB_TOKEN can read the repository"
                } else {
                    "nothing published yet, or the repository is private: set POPCORN_GITHUB_TOKEN"
                };
                bail!("GitHub answered {code} for {url} ({hint})")
            }
            Err(e) => Err(e).with_context(|| format!("Request to {url} failed")),
        }
    }

    /// The newest published release; with `prerelease` pre-releases count too. Drafts never do.
    pub fn latest(&self, prerelease: bool) -> anyhow::Result<Release> {
        let json = "application/vnd.github+json";
        if !prerelease {
            let url = format!("{API}/repos/{}/releases/latest", self.repo);
            return match self.call(&url, json) {
                Ok(mut response) => Ok(response.body_mut().read_json()?),
                // 404 = no published release yet, or no access to the repository: the repository itself says which.
                Err(_) if self.repo_visible() => bail!(
                    "github.com/{} has no published release yet (drafts never count, pre-releases only with --prerelease)",
                    self.repo
                ),
                Err(e) => Err(e),
            };
        }
        let url = format!("{API}/repos/{}/releases?per_page=30", self.repo);
        let releases: Vec<Release> = self.call(&url, json)?.body_mut().read_json()?;
        releases
            .into_iter()
            .find(|r| !r.draft)
            .with_context(|| format!("No published release in github.com/{}", self.repo))
    }

    /// The release of `tag`, also one that is not the latest (BtbN's rolling `latest` tag).
    pub fn release(&self, tag: &str) -> anyhow::Result<Release> {
        let url = format!("{API}/repos/{}/releases/tags/{tag}", self.repo);
        Ok(self
            .call(&url, "application/vnd.github+json")?
            .body_mut()
            .read_json()?)
    }

    fn repo_visible(&self) -> bool {
        let url = format!("{API}/repos/{}", self.repo);
        self.get(&url, "application/vnd.github+json").call().is_ok()
    }

    /// Downloads into `dest` (via `<dest>.part`) and returns the SHA-256 of the content, hex.
    pub fn download(
        &self,
        asset: &Asset,
        dest: &Path,
        progress: &dyn Fn(u64, u64),
        cancel: &AtomicBool,
    ) -> anyhow::Result<String> {
        let response = self.call(&asset.url, "application/octet-stream")?;
        let total = response.body().content_length().unwrap_or(asset.size);
        let mut reader = response.into_body().into_reader();

        let part = dest.with_extension("part");
        let mut file =
            File::create(&part).with_context(|| format!("Cannot create {}", part.display()))?;
        let mut hasher = Sha256::new();
        let mut buffer = vec![0u8; 256 * 1024];
        let mut done = 0u64;
        loop {
            if cancel.load(Ordering::Relaxed) {
                drop(file);
                let _ = std::fs::remove_file(&part);
                bail!(crate::install::Cancelled);
            }
            let read = reader.read(&mut buffer).context("Download interrupted")?;
            if read == 0 {
                break;
            }
            file.write_all(&buffer[..read])?;
            hasher.update(&buffer[..read]);
            done += read as u64;
            progress(done, total);
        }
        file.sync_all()?;
        drop(file);
        if done != total {
            let _ = std::fs::remove_file(&part);
            bail!(
                "Download of {} ended after {done} of {total} bytes",
                asset.name
            );
        }
        std::fs::rename(&part, dest)?;
        Ok(hex(&hasher.finalize()))
    }
}

pub fn sha256_file(path: &Path) -> anyhow::Result<String> {
    let mut file = File::open(path)?;
    let mut hasher = Sha256::new();
    let mut buffer = vec![0u8; 256 * 1024];
    loop {
        let read = file.read(&mut buffer)?;
        if read == 0 {
            return Ok(hex(&hasher.finalize()));
        }
        hasher.update(&buffer[..read]);
    }
}

fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}
