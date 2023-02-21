using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using MahApps.Metro.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using VideoPlayer.Contracts;

namespace WebSiteRatings
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
        private int _currentVolume = 100;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Core.Initialize();

            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);
            _mediaPlayer.TimeChanged += RecalculateSeekValue;
            _mediaPlayer.LengthChanged += SendLengthEvent;
            WebView.CoreWebView2InitializationCompleted += WebViewInitialized;
            VideoView.MediaPlayer = _mediaPlayer;
        }

        public void LoadMedia(string path)
        {
            var media = new Media(_libVLC, path);
            _mediaPlayer.Play(media);
            PostEvent("track_loaded", new TrackLoadedEvent()
            {
                FileName = Path.GetFileName(path),
            });
        }

        private void WebViewInitialized(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            WebView.CoreWebView2.WebMessageReceived += WebMessageReceived;

        }

        private bool PlayerInitialized()
        {
            var path = @"C:\Users\brazi\Desktop\naruto.mkv";
            LoadMedia(path);
            return true;
        }

        private void WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var message = e.WebMessageAsJson;
            var webEvent = JsonSerializer.Deserialize<WebEvent>(message);

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

            WebView?.Dispatcher?.Invoke(() => WebView?.CoreWebView2?.PostWebMessageAsJson(json));
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
                //TODO log
                return false;
            }
            return true;
        }

        private bool SetFullscreen(object data)
        {
            var path = @"C:\Users\brazi\Desktop\naruto.mkv";
            LoadMedia(path);
            return false;

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
                //TODO log
                return false;
            }
            return true;
        }

        private bool SetPlaybackTime(object data)
        {
            try
            {
                var time = (long)Math.Round(((JsonElement)data).GetDouble());
                _mediaPlayer.Time = time;
            }
            catch (Exception e)
            {
                //TODO log
                return false;
            }
            return true;
        }

        private bool SetPlaybackTimeSkip(object data)
        {
            try
            {
                var time = ((JsonElement)data).GetInt64();
                _mediaPlayer.Time += time * 1000;
            }
            catch (Exception e)
            {
                //TODO log
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
                //TODO log
                return false;
            }
            return true;
        }

        private bool PlayNextTitle()
        {
            //TODO
            return false;
        }


        /* 

     private void RecalculateVolumeIcon()
     {
         var volume = _mediaPlayer?.Volume ?? 100;
         VolumeMute.Visibility = Visibility.Hidden;
         VolumeMin.Visibility = Visibility.Hidden;
         VolumeMed.Visibility = Visibility.Hidden;
         VolumeMax.Visibility = Visibility.Hidden;
         if (volume == 0)
         {
             VolumeMute.Visibility = Visibility.Visible;
         } else if (volume <= 33)
         {
             VolumeMute.Visibility = Visibility.Visible;
         }
         else if (volume <= 66)
         {
             VolumeMed.Visibility = Visibility.Visible;
         }
         else
         {
             VolumeMax.Visibility = Visibility.Visible;
         }
     }
        */
    }
}
