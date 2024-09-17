using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using PhotinoNET;

namespace Popcorn.Player.Views;

public class PhotinoHost : NativeControlHost
{
	private readonly IDisposable attacher;
        private readonly BehaviorSubject<IPlatformHandle?> platformHandles = new(null);

        public IPlatformHandle? PlatformHandle { get; private set; }
        public static readonly StyledProperty<object?> ContentProperty =
            ContentControl.ContentProperty.AddOwner<PhotinoHost>();

        private Window? _floatingContent;
        private IDisposable? _disposables;
        private bool _isAttached;
        private IDisposable? _isEffectivelyVisible;

        public PhotinoHost()
        {
            attacher = platformHandles.Subscribe(handle =>
            {
                if (handle is not null)
                {
                    CreatePhotinoWithHandle(handle);
                }
            });

            IsVisibleProperty.Changed.AddClassHandler<PhotinoHost>((s, e) => s.ShowNativeOverlay(s.IsVisible));
        }

        private void CreatePhotinoWithHandle(IPlatformHandle handle)
        {
            //TODO now it's dangerous, since on every handle change new window spawns. Will fix later. Need to implement changing handle on spawned window.
            Task.Run(() =>
            {
                var window = new PhotinoWindow(null, handle.Handle)
                    .SetTitle("Photino Embedded")
                    .SetResizable(true)
                    .SetSize(1024, 800)
                    //.SetChromeless(true)
                    .Load("wwwroot/index.html");

                window.WaitForClose();
            });
        }

        private void InitializeNativeOverlay()
        {
            if (!((Visual)this).IsAttachedToVisualTree()) return;

            if (_floatingContent == null)
            {
                _floatingContent = new Window
                {
                    SystemDecorations = SystemDecorations.None,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                    Background = Brushes.Red,  // Change color here to test
                    SizeToContent = SizeToContent.WidthAndHeight,
                    CanResize = false,
                    ShowInTaskbar = false,
                    ZIndex = Int32.MaxValue,
                    Opacity = 1,
                };

                _disposables = new CompositeDisposable
                {
                    _floatingContent.Bind(Window.ContentProperty, this.GetObservable(ContentProperty)),
                    this.GetObservable(ContentProperty).Skip(1).Subscribe(_=> UpdateOverlayPosition()),
                    this.GetObservable(BoundsProperty).Skip(1).Subscribe(_ => UpdateOverlayPosition()),
                    Observable.FromEventPattern(VisualRoot!, nameof(Window.PositionChanged))
                        .Subscribe(_ => UpdateOverlayPosition())
                };
            }

            ShowNativeOverlay(IsEffectivelyVisible);
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            var handle = base.CreateNativeControlCore(parent);
            platformHandles.OnNext(handle);
            PlatformHandle = handle;
            return handle;
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            attacher.Dispose();
            base.DestroyNativeControlCore(control);
        }

        private void ShowNativeOverlay(bool show)
        {
            if (_floatingContent != null && _floatingContent.IsVisible != show)
            {
                if (VisualRoot is Window window)
                {
                    if (show && _isAttached)
                        _floatingContent.Show(window);
                    else
                        _floatingContent.Hide();
                }
            }
        }

        private void UpdateOverlayPosition()
        {
            if(_floatingContent == null) return;

            bool forceSetWidth = false, forceSetHeight = false;

            var topLeft = new Point();

            var child = _floatingContent.Presenter?.Child;

            if(child?.IsArrangeValid == true)
            {
                switch(child.HorizontalAlignment)
                {
                    case HorizontalAlignment.Left:
                        topLeft = topLeft.WithX(Bounds.Width - _floatingContent.Bounds.Width);
                        break;

                    case HorizontalAlignment.Center:
                        topLeft = topLeft.WithX((Bounds.Width - _floatingContent.Bounds.Width) / 2);
                        break;

                    case HorizontalAlignment.Stretch:
                        forceSetWidth = true;
                        break;
                }

                switch(child.VerticalAlignment)
                {
                    case VerticalAlignment.Bottom:
                        topLeft = topLeft.WithY(Bounds.Height - _floatingContent.Bounds.Height);
                        break;

                    case VerticalAlignment.Center:
                        topLeft = topLeft.WithY((Bounds.Height - _floatingContent.Bounds.Height) / 2);
                        break;

                    case VerticalAlignment.Stretch:
                        forceSetHeight = true;
                        break;
                }
            }

            if(forceSetWidth && forceSetHeight)
                _floatingContent.SizeToContent = SizeToContent.Manual;
            else if(forceSetHeight)
                _floatingContent.SizeToContent = SizeToContent.Width;
            else if(forceSetWidth)
                _floatingContent.SizeToContent = SizeToContent.Height;
            else
                _floatingContent.SizeToContent = SizeToContent.Manual;

            _floatingContent.Width = forceSetWidth ? Bounds.Width : double.NaN;
            _floatingContent.Height = forceSetHeight ? Bounds.Height : double.NaN;

            _floatingContent.MaxWidth = Bounds.Width;
            _floatingContent.MaxHeight = Bounds.Height;

            var newPosition = this.PointToScreen(topLeft);

            if(newPosition != _floatingContent.Position)
            {
                _floatingContent.Position = newPosition;
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _isAttached = true;
            InitializeNativeOverlay();

            _isEffectivelyVisible = this.GetVisualAncestors().OfType<Control>()
                .Select(v => v.GetObservable(IsVisibleProperty))
                .CombineLatest(v => !v.Any(o => !o))
                .DistinctUntilChanged()
                .Subscribe(v => IsVisible = v);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _isEffectivelyVisible?.Dispose();
            ShowNativeOverlay(false);
            _isAttached = false;
        }

        protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromLogicalTree(e);
            _disposables?.Dispose();
            _disposables = null;
            _floatingContent?.Close();
            _floatingContent = null;
        }

    // protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    // {
    //     var parentHandle = parent.Handle;
    //     CreateFloatingWindow();
    //     var floatingHandle = _floatingContent!.TryGetPlatformHandle();
    //     
    //     //var tcs = new TaskCompletionSource<bool>();
    //     //PhotinoWindow.WindowCreated += (sender, e) => tcs.SetResult(true);
    //
    //     // Start WaitForClose in a separate task to avoid blocking
    //     
    //     // Task.Run(() =>
    //     // {
    //     //     var window = new PhotinoWindow(null, floatingHandle.Handle)
    //     //         .SetTitle("Photino Embedded")
    //     //         .SetResizable(true)
    //     //         .SetSize(1024, 800)
    //     //         .Load("wwwroot/index.html");
    //     //
    //     //     window.WaitForClose();
    //     // });
    //
    //     // Wait for the window to be created and handle to be available
    //    // tcs.Task.Wait(); // Deadlocks here, it should be able to access window handle when this event is published
    //
    //     //platformHandles.OnNext(new PlatformHandle(_photinoHandle, "Photino"));
    //
    //     return new PlatformHandle(floatingHandle.Handle, "Photino");
    // }
}
