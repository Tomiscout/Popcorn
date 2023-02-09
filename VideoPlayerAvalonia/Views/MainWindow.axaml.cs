using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LibVLCSharp.Shared;
using System;
using VideoPlayerAvalonia.ViewModels;

namespace VideoPlayerAvalonia.Views
{
    public partial class MainWindow : Window
    {
        LibVLC _libVLC;
        MediaPlayer _mediaPlayer;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        
        public void OnOpened(object sender, EventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            vm?.Play();
            RecalculatePlayImageStatus();

        }

        private void RecalculatePlayImageStatus()
        {
            /*if (_mediaPlayer.IsPlaying)
            {
                PlayImage.Visibility = Visibility.Hidden;
                StopImage.Visibility = Visibility.Visible;
            }
            else
            {
                PlayImage.Visibility = Visibility.Visible;
                StopImage.Visibility = Visibility.Hidden;
            }*/
        }

        private void Play_Clicked(object sender, RoutedEventArgs e)
        {
            /*if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
            }
            else
            {
                _mediaPlayer.Play();
            }
            RecalculatePlayImageStatus();*/
        }
    }
}
