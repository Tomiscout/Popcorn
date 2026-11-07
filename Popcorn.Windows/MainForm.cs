using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Serilog;

namespace Popcorn.Windows;

/// <summary>
/// The app window. Shows the web UI once the server answers, a waiting page until then and again when the
/// connection is lost (then it only waits: the server may have been stopped on purpose, e.g. tray Exit).
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(0x24, 0x24, 0x24);
    private static readonly string Version =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";

    private readonly ServerTarget _target;
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
    private readonly Stopwatch _started = Stopwatch.StartNew();

    /// <summary>Page of <c>--open-path</c> to show instead of the start page, once.</summary>
    private string? _requestedPage;
    private bool _showingWaitPage;
    private bool _watching;
    private bool _appLoadedOnce;
    private string _status = string.Empty;

    private bool _fullscreen;
    private FormWindowState _windowStateBeforeFullscreen;

    public MainForm(ServerTarget target, string? requestedPage)
    {
        _target = target;
        _requestedPage = requestedPage;

        Text = "Popcorn";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BackColor = Background;
        ClientSize = new Size(1280, 720);
        MinimumSize = new Size(480, 320);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_webView);

        Load += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.DataFolder, "webview2"));
            await _webView.EnsureCoreWebView2Async(environment);
        }
        catch (Exception e)
        {
            Log.Fatal(e, "WebView2 failed to start");
            MessageBox.Show(this,
                $"Could not start Microsoft Edge WebView2. Install the WebView2 Runtime and try again.\n\n{e.Message}",
                "Popcorn", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var core = _webView.CoreWebView2;
        // The web UI checks for `PopcornDesktop/` to drop the built-in web player (utils/platform.ts).
        core.Settings.UserAgent = $"{core.Settings.UserAgent} PopcornDesktop/{Version}";
        core.Settings.IsStatusBarEnabled = false;
        core.NewWindowRequested += OnNewWindowRequested;
        core.ContainsFullScreenElementChanged += (_, _) => SetFullscreen(core.ContainsFullScreenElement);
        core.NavigationCompleted += OnNavigationCompleted;
        // F11 (the WinForms control doesn't expose accelerator keys): the page reports it.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            "addEventListener('keydown', e => { if (e.key === 'F11') { e.preventDefault(); chrome.webview.postMessage('toggle-fullscreen'); } }, true);");
        core.WebMessageReceived += (_, e) =>
        {
            if (e.WebMessageAsJson == "\"toggle-fullscreen\"")
            {
                SetFullscreen(!_fullscreen);
            }
        };

        if (await _target.IsReachableAsync())
        {
            Navigate(StartUrl());
        }
        else
        {
            ShowWaitPage();
            WatchServer(startServer: !_target.Explicit);
        }
    }

    /// <summary>A second launch: come to the front and show its page, if it asked for one.</summary>
    public void OnRelaunch(string page)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        Activate();

        if (!Program.IsAppPage(page))
        {
            return;
        }

        _requestedPage = page;
        if (!_showingWaitPage && _webView.CoreWebView2 is not null)
        {
            Navigate(StartUrl());
        }
    }

    /// <summary>The requested page, once; otherwise the app's start page.</summary>
    private string StartUrl()
    {
        var page = _requestedPage;
        _requestedPage = null;
        return page is null ? _target.AppUrl : _target.PageUrl(page);
    }

    private void Navigate(string url)
    {
        _showingWaitPage = false;
        _webView.CoreWebView2.Navigate(url);
    }

    /// <summary>Polls the server until it answers, then loads the app. Optionally starts Popcorn.Server first.</summary>
    private async void WatchServer(bool startServer)
    {
        if (_watching)
        {
            return;
        }
        _watching = true;

        try
        {
            if (startServer)
            {
                StartServer();
            }

            var begin = Stopwatch.StartNew();
            var warned = false;
            while (!await _target.IsReachableAsync())
            {
                if (!warned && begin.Elapsed > TimeSpan.FromSeconds(10))
                {
                    warned = true;
                    Log.Warning("Server at {Url} still not reachable", _target.Url);
                    SetStatus($"No response from {_target.Url}. Still trying…");
                }
                await Task.Delay(250);
            }

            Log.Information("Server reachable at {Url} after {Ms} ms", _target.Url, _started.ElapsedMilliseconds);
            if (_showingWaitPage)
            {
                Navigate(StartUrl());
            }
        }
        finally
        {
            _watching = false;
        }
    }

    private void StartServer()
    {
        SetStatus("Starting Popcorn.Server…");
        try
        {
            var server = ServerTarget.StartServer();
            Log.Information("Started Popcorn.Server (pid {Pid})", server.Id);
            server.EnableRaisingEvents = true;
            server.Exited += (_, _) =>
            {
                var code = server.ExitCode;
                server.Dispose();
                // 0 = another server already runs (single instance) or a clean stop: keep waiting for it.
                if (code != 0)
                {
                    Log.Warning("Popcorn.Server exited with {Code}", code);
                    BeginInvoke(() => SetStatus(
                        $"Popcorn.Server exited (code {code}). See {Path.Combine(Program.DataFolder, "logs")} for details."));
                }
            };
        }
        catch (Exception e)
        {
            Log.Error(e, "Cannot start Popcorn.Server");
            SetStatus($"{e.Message}. Reinstall Popcorn or start Popcorn.Server.exe manually.");
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_showingWaitPage)
        {
            ApplyStatus();
            return;
        }

        if (e.IsSuccess)
        {
            if (!_appLoadedOnce)
            {
                _appLoadedOnce = true;
                Log.Information("UI loaded after {Ms} ms", _started.ElapsedMilliseconds);
            }
            return;
        }

        if (e.WebErrorStatus is CoreWebView2WebErrorStatus.CannotConnect or CoreWebView2WebErrorStatus.ConnectionAborted
            or CoreWebView2WebErrorStatus.ConnectionReset or CoreWebView2WebErrorStatus.Disconnected
            or CoreWebView2WebErrorStatus.ServerUnreachable or CoreWebView2WebErrorStatus.Timeout
            or CoreWebView2WebErrorStatus.HostNameNotResolved)
        {
            // Server went away (restart, crash, stopped): wait for it, don't start one.
            Log.Warning("Failed to load the app: {Status}", e.WebErrorStatus);
            _status = $"Lost connection to {_target.Url}";
            ShowWaitPage();
            WatchServer(startServer: false);
        }
    }

    private void ShowWaitPage()
    {
        _showingWaitPage = true;
        _webView.CoreWebView2.NavigateToString(WaitPage.Html(_status));
    }

    private void SetStatus(string status)
    {
        _status = status;
        ApplyStatus();
    }

    /// <summary>Pushes the status line into the waiting page.</summary>
    private void ApplyStatus()
    {
        if (_showingWaitPage && _webView.CoreWebView2 is { } core)
        {
            _ = core.ExecuteScriptAsync($"document.getElementById('status').textContent = {JsonSerializer.Serialize(_status)};");
        }
    }

    /// <summary>target=_blank / window.open: the user's browser instead of a WebView2 window.</summary>
    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Log.Information("Opening {Url} externally", e.Uri);
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }

    /// <summary>HTML5 fullscreen (video player) and F11: borderless window over the whole screen.</summary>
    private void SetFullscreen(bool fullscreen)
    {
        if (fullscreen == _fullscreen)
        {
            return;
        }
        _fullscreen = fullscreen;

        if (fullscreen)
        {
            _windowStateBeforeFullscreen = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            // Maximizing a borderless window covers the taskbar; it has to go through Normal to re-apply.
            WindowState = FormWindowState.Normal;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = _windowStateBeforeFullscreen;
        }
    }
}
