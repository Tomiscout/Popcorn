# Builds the Windows bundle: Popcorn.Server (serves Popcorn.Web UI, tray icon) + Popcorn.Windows (WebView2 app window)
# + Popcorn.Player.Windows (MPV player). Install it with install_windows.ps1.
# Needs the .NET 10 SDK, Node/npm, git and Rust (cargo, MSVC toolchain) on PATH; libmpv (MPV/src, slow the first time) is
# built here.
# Stops at the first failure.
# On a fresh PC scripts may be blocked: powershell -ExecutionPolicy Bypass -File .\build_windows.ps1
# Layout: <bundle>\Popcorn.Server.exe and Popcorn.Windows.exe side by side (the tray and the app find each other there),
# <bundle>\Player\Popcorn.Player.Windows.exe (WindowsPlayerService; the player's "Find file" looks one folder up).
# Version: -Version, default the VERSION file; stamped into the .NET assemblies and <bundle>\release.json (what
# Popcorn.Installer compares releases with). Rust: the player's native layer (Popcorn.Player.Windows.Native, built by the
# player's .NET build) and Popcorn.Installer.exe.
param(
    [string]$Version
)
$ErrorActionPreference = "Stop"

$ServerDir = "./Popcorn.Server"
$PopcornWebDir = "./Popcorn.Web"
$PlayerDir = "./Popcorn.Player.Windows"
$DesktopDir = "./Popcorn.Windows"
$MpvDir = "./MPV"
$MpvSubmodule = "MPV/src"
$MpvOutputDir = "./MPV/windows"
$Msys2Dir = "./MPV/msys64"
$Msys2Url = "https://github.com/msys2/msys2-installer/releases/download/2026-09-27/msys2-base-x86_64-20260927.sfx.exe"
$Msys2Sha256 = "ad336cccfda47758b5e15cda993fbba421115cb0b126697daef1ee4dfe37209f"
$InstallerDir = "./Popcorn.Installer"
$Platform = "windows-x64"
$BundleDir = "./Bundle/Windows"
$PlayerBundleDir = "./Bundle/Windows/Player"
$PublishDir = "./Bundle/Publish"
$Config = "Release"
$RuntimeId = "win-x64"

# Native tools report failure through the exit code, and stderr output must not count as a failure (git, npm warnings).
function Invoke-Native {
    param ([string]$exe, [Parameter(ValueFromRemainingArguments)][string[]]$arguments)
    $ErrorActionPreference = "Continue"
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$exe $arguments' failed with exit code $LASTEXITCODE"
    }
}

function Assert-Tools {
    foreach ($tool in "dotnet", "node", "npm", "git", "cargo") {
        if (!(Get-Command $tool -ErrorAction SilentlyContinue)) {
            throw "$tool not found on PATH (needs the .NET 10 SDK, Node/npm, git and Rust: https://rustup.rs; open a new terminal after installing them)"
        }
    }
    Invoke-Native dotnet --version | Out-Null # fails if no installed SDK satisfies global.json
}

function Ensure-Directory {
    param ([string]$dir)
    if (!(Test-Path -Path $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Write-Output "Created directory: $dir"
    }
}

function Build-Npm {
    param ([string]$dir, [string]$script = "build")
    Write-Output "Building $dir ($script)..."
    Push-Location $dir
    try {
        Invoke-Native npm ci
        Invoke-Native npm run $script
    } finally {
        Pop-Location
    }
}

function Resolve-Version {
    $value = $Version
    if (!$value) {
        $value = (Get-Content -Raw -Path "./VERSION").Trim()
    }
    if ($value -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        throw "Version '$value' is not major.minor.patch[-prerelease]"
    }
    return $value
}

function Build-Dotnet {
    param ([string]$dir)
    Write-Output "Building $dir..."
    Push-Location $dir
    try {
        Invoke-Native dotnet restore
        Invoke-Native dotnet build --configuration $Config
    } finally {
        Pop-Location
    }
}

# The apps are published with ReadyToRun (framework-dependent, code precompiled to native): no JIT at start-up, which
# matters most for the file name parser's source-generated regexes. Output in $PublishDir\<project>, cleaned first.
function Publish-Dotnet {
    param ([string]$dir)
    $output = Join-Path $PublishDir (Split-Path -Leaf $dir)
    Write-Output "Publishing $dir..."
    Clean-Directory -dir $output
    Invoke-Native dotnet publish $dir --configuration $Config --runtime $RuntimeId --self-contained false `
        "-p:PublishReadyToRun=true" "-p:Version=$Version" --output $output
}

# Rust, MSVC toolchain (Visual Studio Build Tools, "Desktop development with C++") or windows-gnu with MSYS2's
# binutils on PATH. POPCORN_UPDATE_REPO (owner/name) set here is built in as the release repository.
function Build-Installer {
    Write-Output "Building $InstallerDir..."
    Invoke-Native cargo build --release --manifest-path (Join-Path $InstallerDir "Cargo.toml")
}


# Portable MSYS2 in the gitignored MPV/msys64: no install, no admin rights, nothing outside the repo.
function Install-Msys2 {
    if (Test-Path -Path (Join-Path $Msys2Dir "usr\bin\bash.exe")) {
        return
    }
    Write-Output "Downloading MSYS2..."
    $archive = Join-Path $MpvDir "msys2-base.sfx.exe"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = "SilentlyContinue"
    Invoke-WebRequest -Uri $Msys2Url -OutFile $archive -UseBasicParsing
    if ((Get-FileHash -Path $archive -Algorithm SHA256).Hash -ne $Msys2Sha256) {
        Remove-Item -Force -Path $archive
        throw "MSYS2 download does not match its checksum: $Msys2Url"
    }
    Invoke-Native $archive "-y" "-o$MpvDir" | Out-Null
    Remove-Item -Force -Path $archive
}

function Invoke-Msys2 {
    param ([string]$command, [switch]$allowFailure)
    $env:MSYSTEM = "UCRT64"
    $env:CHERE_INVOKING = "1"
    $env:MSYS2_PATH_TYPE = "minimal"
    try {
        $bash = (Resolve-Path (Join-Path $Msys2Dir "usr\bin\bash.exe")).Path
        if ($allowFailure) {
            $ErrorActionPreference = "Continue"
            & $bash -lc $command
        } else {
            Invoke-Native $bash "-lc" $command
        }
    } finally {
        Remove-Item Env:MSYSTEM, Env:CHERE_INVOKING, Env:MSYS2_PATH_TYPE
    }
}

# The Windows counterpart of `make mpv`. Only runs when the DLL is missing; delete MPV/windows to rebuild.
function Build-Mpv {
    if (Test-Path -Path (Join-Path $MpvOutputDir "libmpv-2.dll")) {
        return
    }
    Write-Output "Building libmpv..."
    # Only if not checked out yet: an existing one may be on purpose at another commit than the pinned one.
    if (!(Test-Path -Path "$MpvSubmodule/meson.build")) {
        Invoke-Native git submodule update --init $MpvSubmodule
    }
    Install-Msys2
    # The first update of a new MSYS2 replaces its runtime and ends the shell on purpose, build_windows.sh continues it.
    Invoke-Msys2 -command "pacman -Syu --noconfirm" -allowFailure
    Invoke-Msys2 -command "./MPV/build_windows.sh"
    if (!(Test-Path -Path (Join-Path $MpvOutputDir "libmpv-2.dll"))) {
        throw "MPV/build_windows.sh finished without $MpvOutputDir/libmpv-2.dll, see its output above"
    }
}

function Bundle-Windows {
    Write-Output "Bundling Windows builds..."
    Ensure-Directory -dir $BundleDir
    Ensure-Directory -dir $PlayerBundleDir

    # App window first: the server's copies of shared DLLs win.
    Copy-Item -Recurse -Force -Path (Join-Path $PublishDir "Popcorn.Windows\*") -Destination $BundleDir
    Write-Output "Copied Popcorn.Windows build files to $BundleDir"

    Copy-Item -Recurse -Force -Path (Join-Path $PublishDir "Popcorn.Server\*") -Destination $BundleDir
    Write-Output "Copied Popcorn.Server build files to $BundleDir"

    # Includes popcorn_player.dll (its native layer, built with the player)
    Copy-Item -Recurse -Force -Path (Join-Path $PublishDir "Popcorn.Player.Windows\*") -Destination $PlayerBundleDir
    Write-Output "Copied Popcorn.Player.Windows build files to $PlayerBundleDir"

    Copy-Item -Force -Path (Join-Path $MpvOutputDir "*") -Destination $PlayerBundleDir
    Write-Output "Copied libmpv to $PlayerBundleDir"

    $InstallerExe = Join-Path $InstallerDir "target\release\popcorn-installer.exe"
    Copy-Item -Force -Path $InstallerExe -Destination (Join-Path $BundleDir "Popcorn.Installer.exe")
    Write-Output "Copied Popcorn.Installer to $BundleDir"

    Write-ReleaseManifest
}

# <bundle>\release.json: the installed version for Popcorn.Installer, and the marker of a package zip's root.
function Write-ReleaseManifest {
    $commit = ""
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $commit = (& git rev-parse --short HEAD 2>$null) | Out-String
        $commit = $commit.Trim()
    }
    $manifest = [ordered]@{
        version  = $Version
        platform = $Platform
        commit   = $commit
        date     = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    }
    $path = Join-Path (Resolve-Path $BundleDir).Path "release.json"
    # WriteAllText: UTF-8 without the BOM Set-Content adds in Windows PowerShell.
    [IO.File]::WriteAllText($path, ($manifest | ConvertTo-Json) + "`n")
    Write-Output "Wrote $path ($Version, $Platform)"
}

function Clean-Directory {
    param ([string]$dir)
    if (Test-Path -Path $dir) {
        Remove-Item -Recurse -Force -Path $dir
        Write-Output "Cleaned directory: $dir"
    }
    Ensure-Directory -dir $dir
}

function Assert-Bundle {
    if (!(Test-Path -Path (Join-Path $PlayerBundleDir "libmpv-2.dll"))) {
        throw "libmpv-2.dll is not in $PlayerBundleDir, the player cannot play anything without it"
    }
    if (!(Test-Path -Path (Join-Path $PlayerBundleDir "popcorn_player.dll"))) {
        throw "popcorn_player.dll (Popcorn.Player.Windows.Native) is not in $PlayerBundleDir, the player has no window without it"
    }
}

# Things the bundle needs at runtime but this script does not provide.
function Show-RuntimeWarnings {
    foreach ($tool in "ffmpeg", "ffprobe") {
        if (!(Get-Command $tool -ErrorAction SilentlyContinue)) {
            Write-Warning "$tool not found on PATH, the server needs it at runtime"
        }
    }
}

Push-Location $PSScriptRoot
try {
    Write-Output "Starting build process..."

    Assert-Tools
    $Version = Resolve-Version
    Write-Output "Version $Version"
    Clean-Directory -dir $BundleDir
    Build-Mpv

    Build-Npm -dir $PopcornWebDir                       # post-build copies dist/ into Popcorn.Server/web
    # Bundled plugins: ui/ module first, the .NET build assembles both into Popcorn.Server/plugins/<id>/
    foreach ($pluginDir in Get-ChildItem -Directory "./Plugins") {
        $pluginUi = Join-Path $pluginDir.FullName "ui"
        if (Test-Path (Join-Path $pluginUi "package.json")) { Build-Npm -dir $pluginUi }
        Build-Dotnet -dir $pluginDir.FullName
    }
    Build-Npm -dir $PopcornWebDir -script "build:player" # player overlay -> dist-player/, the Player.Windows build copies it into web-player
    Publish-Dotnet -dir $ServerDir
    Publish-Dotnet -dir $DesktopDir
    Publish-Dotnet -dir $PlayerDir
    Build-Installer
    Bundle-Windows
    Assert-Bundle
    Show-RuntimeWarnings
    Write-Output "Build process completed."
} finally {
    Pop-Location
}
