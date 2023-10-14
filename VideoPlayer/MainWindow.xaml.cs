using LibVLCSharp.WPF;
using MahApps.Metro.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VideoPlayer.Pipes;

namespace VideoPlayer
{
    //https://blogs.msmvps.com/bsonnino/2021/02/27/using-the-new-webview2-in-a-wpf-app/
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    [SupportedOSPlatform("windows")]
    public partial class MainWindow : MetroWindow, IDisposable
    {
        private CancellationTokenSource _ffmpegCts;

        public MainWindow()
        {
            PlayerCore.Initialize();
            PlayerCore.PostWebMessage = PostWebMessageToWebView;
            PlayerCore.PostStreamMessage = PostStreamToWebView;
            PlayerCore.ChangeFullscreen = ChangeFullscreen;
            PlayerCore.PlayerInitialized = PlayerInitialized;
            PlayerCore.SubtitleFileSelect = ShowSubtitleFileDialog;
            InitializeComponent();
        }
        
        private async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Task.Run(async () => await PipeManager.Initialize(this)); //TODO this

            await InitializeWebView2();

            VideoView.MediaPlayer = PlayerCore.GetMediaPlayer();
            Log.Information("MetroWindow initialized");
        }

        private static string AssemblyFolder = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;
        private static string WebPath = $"{AssemblyFolder}\\web-player\\"; //react bundle

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
                } else
                {
                    Log.Information("Using bundled web-player");
                    WebView.CoreWebView2.SetVirtualHostNameToFolderMapping( "app.local", WebPath, CoreWebView2HostResourceAccessKind.Allow );
                    WebView.Source = new Uri($"file:///{WebPath.Replace(@"\", "/")}index.html");
                }
                WebView.Focus();
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to initialize webview");
            }
        }

        private void WebViewInitialized(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            WebView.CoreWebView2.WebMessageReceived += PlayerCore.WebMessageReceived;
        }

        private bool PlayerInitialized()
        {
            PipeManager.Test();

            var envPaths = Environment.GetCommandLineArgs().Where(Path.IsPathFullyQualified).ToList();
            if (envPaths.Any()) {
                envPaths.RemoveAt(0); //DLL path
                PlayerCore.LoadFilesFromInput(envPaths);
            }
            return true;
        }

        private void PostWebMessageToWebView(string json)
        {
            WebView?.Dispatcher?.Invoke(() => WebView?.CoreWebView2?.PostWebMessageAsJson(json));
        }

        private void PostStreamToWebView(string message, byte[] bytes)
        {
            if(bytes == null)
            {
                Log.Warning("Thumb bytes are null");
                return;
            }
            WebView?.Dispatcher?.Invoke(() => {
                using (CoreWebView2SharedBuffer buffer = WebView.CoreWebView2.Environment.CreateSharedBuffer((ulong) bytes.Length))
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

        private bool ChangeFullscreen(bool fullscreen)
        {
            try
            {
                if (fullscreen)
                {
                    UseNoneWindowStyle = true;
                    IgnoreTaskbarOnMaximize = true;
                    ShowMinButton = false;
                    ShowMaxRestoreButton = false;
                    ShowCloseButton = false;
                    WindowState = WindowState.Maximized;
                }
                else
                {
                    UseNoneWindowStyle = false;
                    ShowTitleBar = true;
                    IgnoreTaskbarOnMaximize = false;
                    ShowMinButton = true;
                    ShowMaxRestoreButton = true;
                    ShowCloseButton = true;
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

        public void Dispose()
        {
            PlayerCore.Dispose();
        }
    }
}
