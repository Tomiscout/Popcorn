using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Photino.NET;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia.Data;
using Avalonia.VisualTree;
using Avalonia.Media;

namespace Popcorn.Player.Views;

public class PhotinoHost : NativeControlHost
{
	private Window? _floatingContent;

    private IntPtr _photinoHandle;

    public static readonly DirectProperty<PhotinoHost, PhotinoWindow?> PhotinoWindowProperty =
        AvaloniaProperty.RegisterDirect<PhotinoHost, PhotinoWindow?>(
            nameof(PhotinoWindow),
            o => o.PhotinoWindow,
            (o, v) => o.PhotinoWindow = v,
            defaultBindingMode: BindingMode.TwoWay);

    private readonly IDisposable attacher;
    private readonly BehaviorSubject<PhotinoWindow?> photinoWindows = new(null);
    private readonly BehaviorSubject<IPlatformHandle?> platformHandles = new(null);

    public IPlatformHandle? PlatformHandle { get; private set; }

    public PhotinoHost()
    {
        attacher = platformHandles.WithLatestFrom(photinoWindows).Subscribe(x =>
        {
            if (x.First is not null && x.Second is not null)
            {
                //x.Second.
                // Custom handling to attach Photino window to platform handle
                // x.Second.SetHandle(x.First); // This may need custom implementation
            }
        });

        // Add necessary event handlers and initializations here
    }

    public PhotinoWindow? PhotinoWindow
    {
        get => photinoWindows.Value;
        set => photinoWindows.OnNext(value);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var parentHandle = parent.Handle;

        _photinoWindow = new PhotinoWindow(null, parentHandle)
            .SetTitle("Photino Embedded")
            .SetResizable(true)
            .SetSize(1024, 800)
            .Load("wwwroot/index.html");

        var tcs = new TaskCompletionSource<bool>();
        _photinoWindow.WindowCreated += (sender, e) => tcs.SetResult(true);

        // Start WaitForClose in a separate task to avoid blocking
        Task.Run(() => _photinoWindow.WaitForClose());


        


        // Wait for the window to be created and handle to be available
        // tcs.Task.Wait(); // Uncomment if you need to block until the window is created

        _photinoHandle = _photinoWindow.WindowHandle;
        platformHandles.OnNext(new PlatformHandle(_photinoHandle, "Photino"));
        return new PlatformHandle(_photinoHandle, "Photino");
    }

    private void CreateFloatingWindow() {
        _floatingContent = new Window()
			{
				SystemDecorations = SystemDecorations.None,

				TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
				Background = Brushes.Transparent,

				SizeToContent = SizeToContent.WidthAndHeight,
				CanResize = false,

				ShowInTaskbar = false,

				//Topmost=true,
				ZIndex = Int32.MaxValue,

				Opacity = 1,

			};
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _photinoWindow?.Close();
        _photinoWindow = null;
        _photinoHandle = IntPtr.Zero;
        attacher.Dispose();
        photinoWindows.OnNext(null);
        base.DestroyNativeControlCore(control);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InitializeNativeOverlay();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DestroyNativeControlCore(new PlatformHandle(_photinoHandle, "Photino"));
    }

    private void InitializeNativeOverlay()
    {
        if (!((Visual)this).IsAttachedToVisualTree()) return;

        // Add any overlay initialization logic here if necessary
    }
}
