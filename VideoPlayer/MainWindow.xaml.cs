using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using MahApps.Metro.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Text.Json;
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

            VideoView.MediaPlayer = _mediaPlayer;

            var path = @"C:\Users\brazi\Desktop\naruto.mkv";

            _mediaPlayer.Play(new Media(_libVLC, path));
            /* RecalculatePlayImageStatus();
             RecalculateVolumeIcon();*/

            WebView.CoreWebView2InitializationCompleted += WebViewInitialized;
        }

        private void WebViewInitialized(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            WebView.CoreWebView2.WebMessageReceived += WebMessageReceived;
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
                _ => throw new NotImplementedException()
            };
        }

        private void RecalculateSeekValue(object sender, MediaPlayerTimeChangedEventArgs e)
        {
            //This shouldnt be invoked so much, maybe have timer and send event every 500ms to javascript
            /*TimeSlider.Value = _mediaPlayer.Length > 0 ? e.Time / _mediaPlayer.Length : 0;*/
        }

        private void PostEvent(string eventName, object data)
        {
            var json = JsonSerializer.Serialize(new WebEvent
            {
                Name = eventName,
                Data = data
            });
            WebView?.CoreWebView2?.PostWebMessageAsJson(json);
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
                var time = ((JsonElement)data).GetInt64();
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
                var volume = ((JsonElement)data).GetInt32();
                _mediaPlayer.Volume = volume * 100;
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


        /* private void RecalculatePlayImageStatus()
     {
         if(_mediaPlayer.IsPlaying)
         {
             PlayBtn.Visibility = Visibility.Hidden;
             StopBtn.Visibility = Visibility.Visible;
         }
         else
         {
             PlayBtn.Visibility = Visibility.Visible;
             StopBtn.Visibility = Visibility.Hidden;
         }
     }

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



     private bool ShouldDisplayVolumePopup()
     {
         return true;
     }


     private void Mute_Clicked(object sender, RoutedEventArgs e)
     {
         if (_mediaPlayer.Volume == 0)
         {
             _mediaPlayer.Volume = _currentVolume;
         }
         else
         {
             _currentVolume = _mediaPlayer.Volume;
             _mediaPlayer.Volume = 0;
         }
         RecalculateVolumeIcon();
     }



     private void Forward_Clicked(object sender, RoutedEventArgs e)
     {
         if (!_mediaPlayer.IsPlaying) return;
         _mediaPlayer.Time += 10000;
     }

     private void Backward_Clicked(object sender, RoutedEventArgs e)
     {
         if (!_mediaPlayer.IsPlaying) return;
         _mediaPlayer.Time -= 10000;
     }

     private void NextTrack(object sender, RoutedEventArgs e)
     {
     }

     private void FullscreenToggle(object sender, RoutedEventArgs e)
     {
     }*/
    }
}
