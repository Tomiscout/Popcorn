using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Popcorn.Player.Core.Contracts;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Player.Services;
using Popcorn.Player.Windows.Mpv;
using Popcorn.Player.Windows.Pipe;
using Popcorn.Player.Windows.Utils;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using static Popcorn.Player.Windows.Utils.WindowUtils;

namespace Popcorn.Player.Windows;

//https://blogs.msmvps.com/bsonnino/2021/02/27/using-the-new-webview2-in-a-wpf-app/
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
[SupportedOSPlatform("windows")]
public partial class MainWindow : Window
{
    private readonly WebEventHandler _webEventHandler;
    private WindowChrome _currentChrome;

    public MainWindow()
    {
        _webEventHandler = App.Services.GetService<WebEventHandler>();
        InitializeComponent();

        // Create the MpvPlayerHwndHost and add it to the WPF layout
        var mpvPlayerHost = new MpvPlayerHwndHost();
        MpvPlayerHostBorder.Child = mpvPlayerHost;

        /* PlayerCore.Initialize();
         PlayerCore.PostWebMessage = PostWebMessageToWebView;
         PlayerCore.PostStreamMessage = PostStreamToWebView;
         PlayerCore.ChangeFullscreen = ChangeFullscreen;
         PlayerCore.PlayerInitialized = PlayerInitialized;
         AudioAndSubtitles.SubtitleFileSelect = ShowSubtitleFileDialog;
         InitializeComponent();*/
    }
    
    public async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _currentChrome = WindowChrome.GetWindowChrome(this);
        Task.Run(async () => await App.Services.GetService<ClientPipeManager>().Initialize()); //TODO this is ugly

        GpuScanner.ScanHardware();
        await InitializeWebView2();

        Log.Information("MetroWindow initialized");
    }

    private static string AssemblyFolder = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;
    private static string WebPath = $"{AssemblyFolder}{Path.DirectorySeparatorChar}web-player{Path.DirectorySeparatorChar}"; //react bundle

    private async Task InitializeWebView2()
    {
        WebView.CoreWebView2InitializationCompleted += WebViewInitialized;

        Log.Information("Player web bundle path: {path}", WebPath);
        try
        {
            var op = new CoreWebView2EnvironmentOptions("--disable-web-security");
            var env = await CoreWebView2Environment.CreateAsync(options: op);
            await WebView.EnsureCoreWebView2Async(env);

            bool useLocalWeb = Environment.GetEnvironmentVariable("UseLocalWeb")?.ToLower() == "true";
            if (useLocalWeb)
            {
                Log.Information("Using devloper web-player from localhost");
                WebView.Source = new Uri("http://localhost:3000/");
            }
            else
            {
                Log.Information("Using bundled web-player");
                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping("app.local", WebPath, CoreWebView2HostResourceAccessKind.Allow);
                WebView.Source = new Uri($"file:///{WebPath.Replace(@"\", "/")}index.html");
            }
            WebView.Focus();
            PlayerInitialized();
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to initialize webview");
        }
    }

    private void WebViewInitialized(object sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        WebView.CoreWebView2.WebMessageReceived += WebMessageReceived;
        _webEventHandler.RegisterWindowsMethods(PostWebMessageToWebView, PostStreamToWebView);
    }
    private void WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.WebMessageAsJson;
        var webEvent = JsonSerializer.Deserialize<WebEvent>(message);
        _webEventHandler.OnMessage(webEvent);
    }

    private void PostWebMessageToWebView(string json)
    {
        WebView?.Dispatcher?.Invoke(() => WebView?.CoreWebView2?.PostWebMessageAsJson(json));
    }

    private void PostStreamToWebView(byte[] bytes)
    {
        if (bytes == null)
        {
            Log.Warning("Thumb bytes are null");
            return;
        }
        WebView?.Dispatcher?.Invoke(() => {
            using (CoreWebView2SharedBuffer buffer = WebView.CoreWebView2.Environment.CreateSharedBuffer((ulong)bytes.Length))
            {
                using (Stream stream = buffer.OpenStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                }
                // Notify JS side to read the buffer
                WebView.CoreWebView2.PostSharedBufferToScript(buffer, CoreWebView2SharedBufferAccess.ReadOnly, "");
            }
        });
    }

    // TODO could potentially optimize startup time, since it wont load file until ui is 
    // Currently player (debug) loads in 2.4 seconds total, pipe init takes ~1 second, so can load media item earlier
    private bool PlayerInitialized()
    {
        var envPaths = Environment.GetCommandLineArgs().Skip(1).ToList();
        if(!envPaths.Any())
        {
            return true;
        }

        var playerCore = App.Services.GetService<PlayerCore>();
        if (Guid.TryParse(envPaths.First(), out Guid parsedFileId))
        {
            playerCore.LoadFileId(parsedFileId);
        }
        else
        {
            envPaths = envPaths.Where(p => Path.IsPathFullyQualified(p) && !p.StartsWith("-")).ToList();
            playerCore.LoadFilesFromInput(envPaths);
        }
        return true;
    }

    private void PostStreamToWebView(string message, byte[] bytes)
    {
        if (bytes == null)
        {
            Log.Warning("Thumb bytes are null");
            return;
        }
        WebView?.Dispatcher?.Invoke(() =>
        {
            using (CoreWebView2SharedBuffer buffer = WebView.CoreWebView2.Environment.CreateSharedBuffer((ulong)bytes.Length))
            {
                using (Stream stream = buffer.OpenStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                }
                // Notify JS side to read the buffer
                WebView.CoreWebView2.PostSharedBufferToScript(buffer, CoreWebView2SharedBufferAccess.ReadOnly, message);
            }
        });
    }


    // Frontend 
    private static bool _fullscreenRequested = false;

    public bool ChangeFullscreen(bool fullscreen)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;

            if (fullscreen)
            {
                TitleBar.Visibility = Visibility.Hidden;
                TitleBarRow.Height = new GridLength(0);
                WindowChrome.SetWindowChrome(this, null);

                Topmost = true;

                _fullscreenRequested = true;
                WindowState = WindowState.Maximized;
                SetWindowPopupStyle(fullscreen, handle);

                Topmost = false;
            }
            else
            {
                TitleBar.Visibility = Visibility.Visible;
                TitleBarRow.Height = GridLength.Auto;
                SetWindowPopupStyle(fullscreen, handle);

                WindowState = WindowState.Normal;
                WindowChrome.SetWindowChrome(this, _currentChrome);

                SetDefaultSize(GetScreenWorkingArea(this));
            }

            _webEventHandler.PostEvent("fullscreen", fullscreen);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to change fullscreen status");
            return false;
        }
        return true;
    }

    public void SoftFullscreen(bool force = false)
    {
        var workArea = WindowUtils.GetScreenWorkingArea(this);
        var currentSize = WindowUtils.GetCurrentWindowRect(this);

        if(force || workArea != currentSize)
        {
            Width = workArea.Width;
            Height = workArea.Height;
            Top = workArea.Top;
            Left = workArea.Left;
        } else
        {
            SetDefaultSize(workArea);
        }
    }

    public void SetDefaultSize(WindowRect workArea)
    {
        Width = 1000;
        Height = 650;
        Top = workArea.Top + (workArea.Height - Height) / 2;
        Left = workArea.Left + (workArea.Width - Width) / 2;
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (!_fullscreenRequested && WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            SoftFullscreen(true);
        }
        _fullscreenRequested = false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                SoftFullscreen();
            } else
            {
                DragMove();
            }
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ChangeFullscreen(WindowState != WindowState.Maximized);
    }

    protected override void OnClosed(EventArgs e)
    {
        App.GracefulShutdown();
    }
}
