using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Popcorn.Player.Core;
using Popcorn.Player.ViewModels;

namespace Popcorn.Player.Views;

public partial class VideoPlayer : UserControl
{

    private static string AssemblyFolder = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;
    private static string WebPath = $"{AssemblyFolder}{Path.DirectorySeparatorChar}web-player{Path.DirectorySeparatorChar}"; //react bundle

    public VideoPlayer()
    {
        Initialized += OnInitialized;
        InitializeComponent();
    }

    private void OnInitialized(object sender, System.EventArgs e)
    {
        PlayerCore.Initialize();
        /*PlayerCore.PostWebMessage = PostWebMessageToWebView;
        PlayerCore.PostStreamMessage = PostStreamToWebView;
        PlayerCore.ChangeFullscreen = ChangeFullscreen;*/
        PlayerCore.PlayerInitialized = PlayerInitialized;
        //AudioAndSubtitles.SubtitleFileSelect = ShowSubtitleFileDialog;

        VideoView.MediaPlayer = PlayerCore.MediaPlayer;
        PlayerCore.LoadFilesFromInput(new() {"/media/windows/Users/Tomas/Downloads/Torrent/TEST/dub.mp4"});
    }

    // TODO could potentially optimize startup time, since it wont load file until ui is 
    // Currently player (debug) loads in 2.4 seconds total, pipe init takes ~1 second, so can load media item earlier
    private bool PlayerInitialized()
    {
        var envPaths = Environment.GetCommandLineArgs().Where(Path.IsPathFullyQualified).ToList();
        if (envPaths.Any())
        {
            envPaths.RemoveAt(0); //DLL path
            PlayerCore.LoadFilesFromInput(envPaths.Where(arg => !arg.StartsWith("-")).ToList());
        }
        return true;
    }
}