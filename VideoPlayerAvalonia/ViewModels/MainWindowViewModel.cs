using Avalonia.Controls.ApplicationLifetimes;
using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace VideoPlayerAvalonia.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly LibVLC _libVlc = new LibVLC();
        public MainWindowViewModel()
        {
            MediaPlayer = new MediaPlayer(_libVlc);
        }

        public void Play()
        {
            var args = Environment.GetCommandLineArgs();
            
            var path = @"C:\Users\brazi\Desktop\naruto.mkv";
            using var media = new Media(_libVlc, path);
            MediaPlayer.Play(media);
        }

        public MediaPlayer MediaPlayer { get; }

        public void Dispose()
        {
            MediaPlayer?.Dispose();
            _libVlc?.Dispose();
        }
    }
}
