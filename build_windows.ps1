$PopcornDir = "./Popcorn"
$PopcornPlayerDir = "./Popcorn.Player.Windows"
$PopcornWebDir = "./Popcorn.Player.Web"
$BundleDir = "./Bundle/Windows"
$PlayerBundleDir = "./Bundle/Windows/Player"
$Config = "Release"

function Ensure-Directory {
    param (
        [string]$dir
    )
    if (!(Test-Path -Path $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Write-Output "Created directory: $dir"
    }
}

function Build-Player-Web {
    Write-Output "Building Player Web..."
    Push-Location $PopcornWebDir
    npm run build
    Pop-Location
}

function Build-Popcorn-Windows {
    Write-Output "Building Popcorn Windows..."
    Push-Location $PopcornDir
    dotnet restore
    dotnet build --configuration $Config
    Pop-Location
}

function Build-Popcorn-Player-Windows {
    Write-Output "Building Popcorn Player Windows..."
    Push-Location $PopcornPlayerDir
    dotnet restore
    dotnet build --configuration $Config
    Pop-Location
}

function Bundle-Windows {
    Write-Output "Bundling Windows builds..."

    # Ensure the target directory exists
    Ensure-Directory -dir $BundleDir
    Ensure-Directory -dir $PlayerBundleDir

    # Copy files from the Popcorn project
    $PopcornBuildPath = Join-Path -Path $PopcornDir -ChildPath "bin\$Config\$DotNetVersion\net8.0-windows10.0.19041.0\win10-x64\*"
    Copy-Item -Recurse -Force -Path $PopcornBuildPath -Destination $BundleDir
    Write-Output "Copied Popcorn build files to $BundleDir"

    # Copy files from the Popcorn Player project
    $PopcornPlayerBuildPath = Join-Path -Path $PopcornPlayerDir -ChildPath "bin\$Config\$DotNetVersion\net8.0-windows10.0.19041.0\*"
    Copy-Item -Recurse -Force -Path $PopcornPlayerBuildPath -Destination $PlayerBundleDir
    Write-Output "Copied Popcorn Player build files to $BundleDir"
}

function Clean-Directory {
    param (
        [string]$dir
    )
    if (Test-Path -Path $dir) {
        Remove-Item -Recurse -Force -Path $dir
        Write-Output "Cleaned directory: $dir"
    }
    # Ensure the directory exists after cleaning
    Ensure-Directory -dir $dir
}

# Main script to build all
Write-Output "Starting build process..."

Clean-Directory -dir $BundleDir

Build-Player-Web
Build-Popcorn-Windows
Build-Popcorn-Player-Windows
Bundle-Windows
Write-Output "Build process completed."
