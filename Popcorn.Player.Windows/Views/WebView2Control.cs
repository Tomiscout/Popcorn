using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Popcorn.Player.Windows.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Interop;
using Microsoft.Web.WebView2.Core;
using Popcorn.Player.Windows.Utils;
using Serilog;
using System;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

public class WebView2Control : NativeControlHost
{
    private CoreWebView2Controller _controller;
    private CoreWebView2Environment _environment;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var hwnd = parent.Handle;

        InitializeWebViewAsync(hwnd);

        return parent;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _controller.Close();
        _controller = null;
        base.DestroyNativeControlCore(control);
    }

    private static string AssemblyFolder = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;
    private static string WebPath = $"{AssemblyFolder}{Path.DirectorySeparatorChar}web-player{Path.DirectorySeparatorChar}"; //react bundle

    private async Task InitializeWebViewAsync(IntPtr hwnd)
    {
        try
        {
            var op = new CoreWebView2EnvironmentOptions("--disable-web-security");
            var env = await CoreWebView2Environment.CreateAsync(options: op);
            _controller = await env.CreateCoreWebView2ControllerAsync(hwnd);
            _controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0, 0, 0, 0);

            // Set HWND as a layered window to allow transparency
            int extendedStyle = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
            Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE, extendedStyle | Win32Api.WS_EX_LAYERED);
            Win32Api.SetLayeredWindowAttributes(hwnd, 0, 0, Win32Api.LWA_COLORKEY);

            _controller.Bounds = new System.Drawing.Rectangle(0, 0, (int)(Bounds.Width * App.CurrentScalingFactor), (int)(Bounds.Height * App.CurrentScalingFactor));
            this.PropertyChanged += (sender, e) =>
            {
                if (e.Property == BoundsProperty && _controller != null)
                {
                    _controller.Bounds = new System.Drawing.Rectangle(
                                0,
                                0,
                                (int)(Bounds.Width * App.CurrentScalingFactor),
                                (int)(Bounds.Height * App.CurrentScalingFactor)
                            );
                }
            };

            bool useLocalWeb = Environment.GetEnvironmentVariable("UseLocalWeb")?.ToLower() == "true";
            if (useLocalWeb)
            {
                Log.Information("Using devloper web-player from localhost");
                _controller.CoreWebView2.Navigate("http://localhost:3000/");
            }
            else
            {
                Log.Information("Using bundled web-player");
                _controller.CoreWebView2.SetVirtualHostNameToFolderMapping("app.local", WebPath, CoreWebView2HostResourceAccessKind.Allow);
                _controller.CoreWebView2.Navigate($"file:///{WebPath.Replace(@"\", "/")}index.html");
            }

            BringWebViewToFront(hwnd);
            Focus();
            _controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to initialize webview");
        }
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOP = new IntPtr(0);
    private void BringWebViewToFront(IntPtr hwnd)
    {
        Win32Api.SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }
}
