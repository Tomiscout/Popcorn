using LibVLCSharp.WPF;
using MahApps.Metro.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Popcorn.Player.Core.Contracts;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.Services;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Player.Services;
using Popcorn.Player.Windows.Mpv;
using Popcorn.Player.Windows.Pipe;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace Popcorn.Player.Windows;

//https://blogs.msmvps.com/bsonnino/2021/02/27/using-the-new-webview2-in-a-wpf-app/
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
[SupportedOSPlatform("windows")]
public partial class MainWindow : MetroWindow
{
    private readonly WebEventHandler _webEventHandler;
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
    
    public async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
    {
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
        var envPaths = Environment.GetCommandLineArgs().Where(Path.IsPathFullyQualified).ToList();
        if (envPaths.Any())
        {
            envPaths.RemoveAt(0); //DLL path
            App.Services.GetService<PlayerCore>().LoadFilesFromInput(envPaths.Where(arg => !arg.StartsWith("-")).ToList());
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

    public bool ChangeFullscreen(bool fullscreen)
    {
        try
        {
            if (fullscreen)
            {
                ShowTitleBar = false;
                IgnoreTaskbarOnMaximize = true;
                ShowMinButton = false;
                ShowMaxRestoreButton = false;
                ShowCloseButton = false;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
            else
            {
                ShowTitleBar = true;
                IgnoreTaskbarOnMaximize = false;
                ShowMinButton = true;
                ShowMaxRestoreButton = true;
                ShowCloseButton = true;
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to change fullscreen status");
            return false;
        }
        return true;
    }

    private string ShowSubtitleFileDialog()
    {
        string result = null;
        Dispatcher.Invoke(() =>
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Subtitle files (*.srt, *.sub, *.ssa, *.ass, *.vtt, *.idx, *.rt, *.smil, *.smi)|*.srt;*.sub;*.ssa;*.ass;*.vtt;*.idx;*.rt;*.smil;*.smi";

            if (openFileDialog.ShowDialog() == true)
            {
                result = openFileDialog.FileName;
            }
        });
        return result;
    }

    protected override void OnClosed(EventArgs e)
    {
        App.GracefulShutdown();
    }
}
