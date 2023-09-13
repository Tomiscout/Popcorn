using LibVLCSharp.WPF;
using MahApps.Metro.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
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
        public MainWindow()
        {
            PlayerCore.Initialize();
            PlayerCore.PostWebMessage = PostWebMessageToWebView;
            PlayerCore.ChangeFullscreen = ChangeFullscreen;
            PlayerCore.PlayerInitialized = PlayerInitialized;
            InitializeComponent();
        }
        
        private async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PipeManager.Print = LogInBrowser;

            Task.Run(async () => await PipeManager.Initialize(this)); //TODO this

            await InitializeWebView2();

            VideoView.MediaPlayer = PlayerCore.GetMediaPlayer();
        }

        private async Task InitializeWebView2()
        {
            WebView.CoreWebView2InitializationCompleted += WebViewInitialized;

            var asemblyFolder = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;
            var webPath = $"{asemblyFolder}\\web-player\\build\\"; //React bundle copied by post-build script

            try
            {
                var op = new CoreWebView2EnvironmentOptions("--disable-web-security");
                var env = await CoreWebView2Environment.CreateAsync(options: op);
                await WebView.EnsureCoreWebView2Async(env);

                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app.local",
                    webPath,
                    CoreWebView2HostResourceAccessKind.Allow
                );
                WebView.Source = new Uri($"file:///{webPath.Replace(@"\", "/")}index.html");
                WebView.Focus();
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to initialize webview", e);
            }
        }

        private void WebViewInitialized(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            WebView.CoreWebView2.WebMessageReceived += PlayerCore.WebMessageReceived;
        }

        private bool PlayerInitialized()
        {
            PipeManager.Test();

            var envVars = Environment.GetCommandLineArgs();
            if (envVars.Length > 1) {
                PlayerCore.LoadMedia(envVars[1]);
            }
            return true;
        }

        private void PostWebMessageToWebView(string json)
        {
            WebView?.Dispatcher?.Invoke(() => WebView?.CoreWebView2?.PostWebMessageAsJson(json));
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
                Debug.WriteLine("Failed to change fullscreen status", e);
                return false;
            }
            return true;
        }

        private void LogInBrowser(string message)
        {
            WebView?.Dispatcher?.Invoke(() => WebView?.CoreWebView2?.ExecuteScriptAsync($"console.log('{message}')"));
        }

        public void Dispose()
        {
            PlayerCore.Dispose();
        }
    }
}
