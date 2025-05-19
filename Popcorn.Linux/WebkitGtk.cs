using Gdk;
using WebKit;

namespace Popcorn.Linux;

#pragma warning disable CA1416

public class WebviewGtk
{
    public static WebView CreateWebView(string serverUrl)
    {
        var webView = WebView.New();
        webView.HeightRequest = 300;
        webView.WidthRequest = 1280;
        webView.SetBackgroundColor(new RGBA { Red = 255, Blue = 0, Green = 0, Alpha = 0 });
        
        var webViewSettings = webView.GetSettings();
        webViewSettings.EnableDeveloperExtras = true;
        webViewSettings.EnableCaretBrowsing = false;
        webViewSettings.EnableJavascript = true;
        webViewSettings.EnableWriteConsoleMessagesToStdout = true;
        webViewSettings.EnableBackForwardNavigationGestures = false;
        webViewSettings.MediaPlaybackRequiresUserGesture = false;
        webViewSettings.AllowUniversalAccessFromFileUrls = true;
        webViewSettings.EnableSmoothScrolling = true;
        webViewSettings.HardwareAccelerationPolicy = HardwareAccelerationPolicy.Always;
        webViewSettings.Enable2dCanvasAcceleration = true;
        webView.SetSettings(webViewSettings);

        webView.LoadUri($"{serverUrl}/index.html");

        return webView;
    }
}

#pragma warning enable CA1416
