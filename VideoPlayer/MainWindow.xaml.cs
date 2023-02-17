using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using MahApps.Metro.Controls;
using System;
using System.Windows;
using System.Windows.Controls;

namespace WebSiteRatings
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        LibVLC _libVLC;
        MediaPlayer _mediaPlayer;
        
        public MainWindow()
        {
            InitializeComponent();
            
        }

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Core.Initialize();

            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);

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
    }
}
