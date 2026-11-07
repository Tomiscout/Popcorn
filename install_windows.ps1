# Installs the Windows bundle (build_windows.ps1 -> Bundle/Windows) for the current user; the counterpart of
# `make install-linux`. No Windows service: Popcorn.Server is a plain background process (tray icon, single instance)
# started at login through the HKCU Run key, like the systemd user service on Linux.
#   - stops running Popcorn processes
#   - copies the bundle to %LOCALAPPDATA%\Programs\Popcorn (replaced, user data stays in %LOCALAPPDATA%\Popcorn)
#   - Run key "Popcorn" -> Popcorn.Server.exe, Start menu shortcut "Popcorn" -> Popcorn.Windows.exe (the app window)
#   - registers the app in Settings -> Apps (HKCU Uninstall key); Uninstall runs `Popcorn.Installer.exe --uninstall`
#     (or uninstall.ps1 without any UI when the bundle has no installer) and removes the binaries, the data folder
#     and the entries above
# Popcorn.Installer.exe does the same for end users (installs a GitHub release, plus FFmpeg); this is the dev path.
#   - starts the server
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\Popcorn"),
    [string]$BundleDir = (Join-Path $PSScriptRoot "Bundle\Windows")
)
$ErrorActionPreference = "Stop"

$ServerExe = Join-Path $InstallDir "Popcorn.Server.exe"
$AppExe = Join-Path $InstallDir "Popcorn.Windows.exe"

if (!(Test-Path (Join-Path $BundleDir "Popcorn.Server.exe"))) {
    throw "$BundleDir\Popcorn.Server.exe missing, run build_windows.ps1 first"
}
if (!(& dotnet --list-runtimes 2>$null | Select-String -Quiet '^Microsoft\.AspNetCore\.App 10\.')) {
    throw "ASP.NET Core 10 runtime missing (needed by Popcorn.Server): https://dotnet.microsoft.com/download/dotnet/10.0"
}
if (!(& dotnet --list-runtimes 2>$null | Select-String -Quiet '^Microsoft\.WindowsDesktop\.App 10\.')) {
    throw ".NET 10 Desktop Runtime missing (needed by Popcorn.Windows and the player): https://dotnet.microsoft.com/download/dotnet/10.0"
}

$Processes = "Popcorn.Server", "Popcorn.Windows", "Popcorn.Player.Windows", "Popcorn.Installer"
$Running = Get-Process -Name $Processes -ErrorAction SilentlyContinue
if ($Running) {
    Write-Output "Stopping running Popcorn instances"
    $Running | Stop-Process -Force
    $Running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

if (Test-Path $InstallDir) {
    Remove-Item -Recurse -Force $InstallDir
}
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Recurse -Force -Path (Join-Path $BundleDir "*") -Destination $InstallDir
Write-Output "Installed to $InstallDir"

$RunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Set-ItemProperty -Path $RunKey -Name "Popcorn" -Value "`"$ServerExe`""
Write-Output "Popcorn.Server starts at login ($RunKey\Popcorn)"

$Shortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "Popcorn.lnk"
$Shell = New-Object -ComObject WScript.Shell
$Link = $Shell.CreateShortcut($Shortcut)
$Link.TargetPath = $AppExe
$Link.WorkingDirectory = $InstallDir
$Link.IconLocation = "$AppExe,0"
$Link.Description = "Popcorn"
$Link.Save()
Write-Output "Start menu shortcut: $Shortcut"

$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Popcorn"
$Uninstaller = Join-Path $InstallDir "uninstall.ps1"
$Uninstall = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$Uninstaller`""
$QuietUninstall = $Uninstall
# Same entry Popcorn.Installer writes: its --uninstall asks first (and can keep the data), the script does not.
$InstallerExe = Join-Path $InstallDir "Popcorn.Installer.exe"
if (Test-Path $InstallerExe) {
    $Uninstall = "`"$InstallerExe`" --uninstall"
    $QuietUninstall = "$Uninstall --confirm"
}
$SizeKb = [int]((Get-ChildItem -Recurse -File $InstallDir | Measure-Object -Property Length -Sum).Sum / 1KB)
New-Item -Path $UninstallKey -Force | Out-Null
$Entries = @{
    DisplayName     = "Popcorn"
    DisplayVersion  = ((Get-Item $AppExe).VersionInfo.ProductVersion -split "\+")[0]
    Publisher       = "Popcorn"
    DisplayIcon     = "$AppExe,0"
    InstallLocation = $InstallDir
    InstallDate     = (Get-Date -Format "yyyyMMdd")
    UninstallString = $Uninstall
    QuietUninstallString = $QuietUninstall
}
foreach ($Entry in $Entries.GetEnumerator()) {
    Set-ItemProperty -Path $UninstallKey -Name $Entry.Key -Value $Entry.Value
}
foreach ($Flag in "NoModify", "NoRepair") {
    New-ItemProperty -Path $UninstallKey -Name $Flag -Value 1 -PropertyType DWord -Force | Out-Null
}
New-ItemProperty -Path $UninstallKey -Name "EstimatedSize" -Value $SizeKb -PropertyType DWord -Force | Out-Null
Write-Output "Registered in Settings -> Apps (uninstall removes the program and $env:LOCALAPPDATA\Popcorn)"

Start-Process -FilePath $ServerExe -WorkingDirectory $InstallDir
Write-Output "Started Popcorn.Server"
