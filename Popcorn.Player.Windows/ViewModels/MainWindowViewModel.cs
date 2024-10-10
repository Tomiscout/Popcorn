using Avalonia.Controls;

namespace Popcorn.Player.Windows.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    public MainWindowViewModel()
    {
    }

    public void Play()
    {
        if (Design.IsDesignMode)
        {
            return;
        }

        //using var media = new Media(_libVlc, new Uri("http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4"));
        //MediaPlayer.Play(media);
    }

    public void Stop()
    {
        //MediaPlayer.Stop();
    }

    public void Dispose()
    {
        
    }
}