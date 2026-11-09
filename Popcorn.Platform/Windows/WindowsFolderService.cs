using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;

namespace Popcorn.Platform.Windows;

/// <summary>
/// Windows counterpart of <c>LinuxFolderService</c>. The server is a plain net10.0 app (no WinForms/WPF), so the
/// pickers are shown by a short-lived STA PowerShell process, the same way Linux shells out to zenity.
/// </summary>
public class WindowsFolderService(ILogger<WindowsFolderService> logger) : INativeFolderService
{
    public void OpenFolderInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to open folder {path}", path);
        }
    }

    public Task<string?> OpenFolderPicker() => RunPicker(
        "$d = New-Object System.Windows.Forms.FolderBrowserDialog; " +
        "if ($d.ShowDialog() -eq 'OK') { $d.SelectedPath }");

    public Task<string?> PickImageFile() => RunFilePicker("Select image", "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");

    public Task<string?> PickExecutableFile() => RunFilePicker(
        "Select video player program",
        "Programs (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|All files (*.*)|*.*");

    private Task<string?> RunFilePicker(string title, string filter) => RunPicker(
        "$d = New-Object System.Windows.Forms.OpenFileDialog; " +
        $"$d.Title = '{title}'; $d.Filter = '{filter}'; " +
        "if ($d.ShowDialog() -eq 'OK') { $d.FileName }");

    private async Task<string?> RunPicker(string script)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                ArgumentList =
                {
                    "-NoProfile", "-NonInteractive", "-STA", "-Command",
                    "Add-Type -AssemblyName System.Windows.Forms; " +
                    "[Console]::OutputEncoding = [Text.Encoding]::UTF8; " + script,
                },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var result = output.Trim();
            return process.ExitCode == 0 && result.Length > 0 ? result : null;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error opening file/folder picker");
            return null;
        }
    }
}
