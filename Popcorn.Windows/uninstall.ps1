# Popcorn uninstaller, run by Settings -> Apps -> Popcorn -> Uninstall (the "UninstallString" install_windows.ps1
# registers under HKCU\...\Uninstall\Popcorn). No UI: removes everything install_windows.ps1 added plus the data folder
# (library database, logs, caches, plugins, download list, WebView2 profile). Downloaded media files
# (Settings -> Downloads folder, default %USERPROFILE%\Downloads\Popcorn) are the user's and stay.
# Lives in the install folder it deletes: PowerShell has read the whole script before it runs.
$ErrorActionPreference = "Continue"

$InstallDir = $PSScriptRoot
$DataDir = Join-Path $env:LOCALAPPDATA "Popcorn"
Set-Location $env:TEMP

# The WebView2 processes of the app window hold files in the data folder until they exit.
$Processes = "Popcorn.Server", "Popcorn.Windows", "Popcorn.Player.Windows", "Popcorn.Installer"
$Running = Get-Process -Name $Processes -ErrorAction SilentlyContinue
if ($Running) {
    $Running | Stop-Process -Force
    $Running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "Popcorn" -ErrorAction SilentlyContinue
# Task Manager's on/off state of that Run value (also written by the Settings page's Startup switch)
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run" -Name "Popcorn" -ErrorAction SilentlyContinue
Remove-Item -Force (Join-Path ([Environment]::GetFolderPath("Programs")) "Popcorn.lnk") -ErrorAction SilentlyContinue

function Remove-Folder([string]$Path) {
    for ($i = 0; $i -lt 10 -and (Test-Path $Path); $i++) {
        Remove-Item -Recurse -Force $Path -ErrorAction SilentlyContinue
        if (Test-Path $Path) { Start-Sleep -Milliseconds 500 }
    }
}
Remove-Folder $DataDir
Remove-Folder $InstallDir

Remove-Item -Recurse -Force "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Popcorn" -ErrorAction SilentlyContinue
