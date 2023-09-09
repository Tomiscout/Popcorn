using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using MahApps.Metro.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using VideoBlazor.Domain.Models;
using VideoPlayer.Contracts;
using VideoPlayer.Domain.Extensions;
using VideoPlayer.Pipes;
using VideoPlayer.Shared.PipeEvents;
using VideoPlayer.Shared.PipeEvents.Server;

namespace VideoPlayer
{
    //https://blogs.msmvps.com/bsonnino/2021/02/27/using-the-new-webview2-in-a-wpf-app/
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    [SupportedOSPlatform("windows")]
    public partial class MainWindow : MetroWindow
    {
        LibVLC _libVLC;
        MediaPlayer _mediaPlayer;
        private static MediaItem _currentMediaItem = null;

        public MainWindow()
        {
            PlayerCore.Initialize();
            InitializeComponent();
        }
        
        private async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PipeManager.Print = LogInBrowser;
            Core.Initialize();

            Task.Run(async () => await PipeManager.Initialize(this)); //TODO this

            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);
            _mediaPlayer.TimeChanged += RecalculateSeekValue;
            _mediaPlayer.LengthChanged += SendLengthEvent;
            _mediaPlayer.EndReached += PlaybackEndReached;
            await InitializeWebView2();

            VideoView.MediaPlayer = _mediaPlayer;
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

        public void LoadMedia(string path)
        {
            var media = new MediaFile //TODO this could be prettier
            {
                Path = path,
                FormatTags = new List<string>() { System.IO.Path.GetExtension(path) }
            };
            media.ParseTitle();
            LoadMedia(media);
        }

        //TODO dublicate code
        public void LoadMedia(MediaFile file)
        {
            if (!File.Exists(file.Path))
            {
                //TODO popover file not found
            }
            var media = new Media(_libVLC, file.Path);
            _mediaPlayer.Play(media);

            PostEvent("track_loaded", new TrackLoadedEvent() //TODO episodes, seasons and such
            {
                FileName = file.Path,
                Title = file.Title,
                ShortTitle = file.GetDisplayTitle()
            });

            //Activate(); For some reason it crashes
        }

        private void WebViewInitialized(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            
            WebView.CoreWebView2.WebMessageReceived += WebMessageReceived;
        }

        private bool PlayerInitialized()
        {
            PipeManager.Test();

            //var path = @"C:\Users\brazi\Desktop\naruto.mkv";
            var envVars = Environment.GetCommandLineArgs();
            if (envVars.Length > 1) {
                LoadMedia(envVars[1]);
            }
            return true;
        }

        private void WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var message = e.WebMessageAsJson;
            var webEvent = JsonSerializer.Deserialize<WebEvent>(message);

            Debug.WriteLine(webEvent.Name);
            var success = webEvent.Name switch
            {
                "play_status_changed" => Play(webEvent.Data),
                "fullscreen" => SetFullscreen(webEvent.Data),
                "set_playback_time" => SetPlaybackTime(webEvent.Data),
                "skip_seconds" => SetPlaybackTimeSkip(webEvent.Data),
                "volume" => SetVolume(webEvent.Data),
                "play_next_title" => PlayNextTitle(),
                "initialized" => PlayerInitialized(),
                _ => throw new NotImplementedException()
            };
        }

        //This is invoked 3-4 times per second
        private void RecalculateSeekValue(object sender, MediaPlayerTimeChangedEventArgs e)
        {
            PostEvent("current_time", e.Time);
        }

        private void SendLengthEvent(object sender, MediaPlayerLengthChangedEventArgs e)
        {
            PostEvent("duration_update", e.Length);
        }

        private void PostEvent(string eventName, object data)
        {
            var json = JsonSerializer.Serialize(new WebEvent
            {
                Name = eventName,
                Data = data
            }, new JsonSerializerOptions()
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            //TODO fix task was canceled exception
            WebView?.Dispatcher?.Invoke(() => WebView?.CoreWebView2?.PostWebMessageAsJson(json));
        }

        public void SetMediaInfo(MediaItem item)
        {
            _currentMediaItem = item;
            //TODO set media info
        }

        private bool Play(object data)
        {
            try
            {
                var play = ((JsonElement)data).GetBoolean();
                if (play)
                {
                    _mediaPlayer.Play();
                }
                else
                {
                    _mediaPlayer.Pause();
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed media player play/pause", e);
                return false;
            }
            return true;
        }

        private bool SetFullscreen(object data)
        {
            /*PipeManager.Test();
            LogInBrowser(_mediaPlayer.State.ToString());*/


            // var path = @"C:\Users\brazi\Desktop\naruto.mkv";
            //LoadMedia(path);

            try
            {
                var fullscreen = ((JsonElement)data).GetBoolean();
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

        private bool SetPlaybackTime(object data)
        {
            try
            {
                var time = (long)Math.Round(double.Parse(((JsonElement)data).GetString()));
                _mediaPlayer.Time = time;
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to set playback time", e);
                return false;
            }
            return true;
        }

        private void PlaybackEndReached(object sender, EventArgs e)
        {
            PostEvent("playback_ended", null);
        }

        private bool SetPlaybackTimeSkip(object data)
        {
            try
            {
                var time = ((JsonElement)data).GetInt64();
                _mediaPlayer.Time += time * 1000;
                PostEvent("current_time", _mediaPlayer.Time);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to playback skip", e);
                return false;
            }
            return true;
        }

        private bool SetVolume(object data)
        {
            try
            {
                var volume = ((JsonElement)data).GetDouble();
                _mediaPlayer.Volume = (int)Math.Floor(volume * 100);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to set volume", e);
                return false;
            }
            return true;
        }

        private bool PlayNextTitle()
        {
            var nextItem = _currentMediaItem?.GetNextMediaFile();
            if (nextItem is null) return false;

            //Update history
            PipeManager.SendTrackPlayedEvent(new TrackPlayedEvent
            {
                ItemId = _currentMediaItem.Id,
                HistoryItem = new PlaybackHistory
                {
                    Filename = nextItem.Path,
                    Title = nextItem.Title,
                    PlaybackTime = DateTime.Now
                }
            }).ConfigureAwait(false);

            LoadMedia(nextItem);

            return true;
        }

        private void LogInBrowser(string message)
        {
            WebView?.Dispatcher?.Invoke(() => WebView?.CoreWebView2?.ExecuteScriptAsync($"console.log('{message}')"));
        }
    }
}
