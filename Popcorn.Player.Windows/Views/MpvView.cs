using Avalonia.Controls;
using Avalonia.Platform;
using Nickvision.MPVSharp;

namespace Popcorn.Player.Windows.Views;
public class MpvView : NativeControlHost
{
    private Client? _player;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var hwnd = parent.Handle;

        Thread.Sleep(2000);
        InitializeMpv(hwnd);

        return parent;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        base.DestroyNativeControlCore(control);

        _player?.Destroy();
        _player = null;
    }

    private void InitializeMpv(IntPtr hwnd)
    {
        _player = new Client();
        _player.SetProperty("vo", "gpu");
        _player.SetProperty("hwdec", "auto");
        _player.SetProperty("osd-level", "0");
        _player.SetOption("sub-border-size", "1");
        //_player.SetProperty("log-file", "mpv_log.log");


        var windowHandle = hwnd != IntPtr.Zero ? ((uint)hwnd.ToInt64()).ToString() : "0";
        _player.SetOption("wid", windowHandle);
        _player.LogMessageReceived += (sender, e) =>
        {
            Console.Write($"[{e.Prefix}] {e.Text}"); // Log messages (e.Text) end with newline char
        };

        _player.Initialize();

        var path = @"C:\Users\Tomas\Downloads\Video samples\hdr_test.mov";
        _player.Command($"loadfile '{path}' append-play");

    }
}
