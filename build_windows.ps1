# Builds the Windows bundle: Popcorn.Server (serves Popcorn.Web UI) + Popcorn.Player.Windows (MPV player).
# Output layout matches what WindowsPlayerService expects: <bundle>\Player\Popcorn.Player.Windows.exe
$ServerDir = "./Popcorn.Server"
$PopcornWebDir = "./Popcorn.Web"
$PlayerDir = "./Popcorn.Player.Windows"
$BundleDir = "./Bundle/Windows"
$PlayerBundleDir = "./Bundle/Windows/Player"
$Config = "Release"

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
    npm ci
    npm run $script
    Pop-Location
}

function Build-Dotnet {
    param ([string]$dir)
    Write-Output "Building $dir..."
    Push-Location $dir
    dotnet restore
    dotnet build --configuration $Config
    Pop-Location
}

function Bundle-Windows {
    Write-Output "Bundling Windows builds..."
    Ensure-Directory -dir $BundleDir
    Ensure-Directory -dir $PlayerBundleDir

    $ServerBuildPath = Join-Path -Path $ServerDir -ChildPath "bin\$Config\net10.0\*"
    Copy-Item -Recurse -Force -Path $ServerBuildPath -Destination $BundleDir
    Write-Output "Copied Popcorn.Server build files to $BundleDir"

    $PlayerBuildPath = Join-Path -Path $PlayerDir -ChildPath "bin\$Config\net8.0-windows10.0.19041.0\*"
    Copy-Item -Recurse -Force -Path $PlayerBuildPath -Destination $PlayerBundleDir
    Write-Output "Copied Popcorn.Player.Windows build files to $PlayerBundleDir"
}

function Clean-Directory {
    param ([string]$dir)
    if (Test-Path -Path $dir) {
        Remove-Item -Recurse -Force -Path $dir
        Write-Output "Cleaned directory: $dir"
    }
    Ensure-Directory -dir $dir
}

Write-Output "Starting build process..."

Clean-Directory -dir $BundleDir

Build-Npm -dir $PopcornWebDir                       # post-build copies dist/ into Popcorn.Server/web
Build-Npm -dir $PopcornWebDir -script "build:player" # player overlay -> dist-player/, Player.Windows post-build copies it into web-player
Build-Dotnet -dir $ServerDir
Build-Dotnet -dir $PlayerDir
Bundle-Windows
Write-Output "Build process completed."
