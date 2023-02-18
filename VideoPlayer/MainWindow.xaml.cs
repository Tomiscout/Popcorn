using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using MahApps.Metro.Controls;
using System;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

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

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            wvc?.CoreWebView2?.Navigate("https://www.microsoft.com");
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
            RecalculatePlayImageStatus();
            RecalculateVolumeIcon();

        }

        private void RecalculatePlayImageStatus()
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

        private void RecalculateSeekValue(object sender, MediaPlayerTimeChangedEventArgs e)
        {
            //This shouldnt be invoked so much, maybe use dispatchtimer for polling every 1sec or something
           /* this.Dispatcher.Invoke(() =>
            {
                TimeSlider.Value = _mediaPlayer.Length > 0 ? e.Time / _mediaPlayer.Length : 0;
            });*/
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

        private void Play_Clicked(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
            }
            else
            {
                _mediaPlayer.Play();
            }
            RecalculatePlayImageStatus();
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
        }
    }
}
