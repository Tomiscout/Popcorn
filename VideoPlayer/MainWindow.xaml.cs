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
        }

        private void Play_Clicked(object sender, RoutedEventArgs e)
        {
        }
    }
}
