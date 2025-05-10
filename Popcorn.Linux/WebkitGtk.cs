using System.IO;
using System.Reflection;
using Gdk;
using Gio;
using WebKit;
using Soup;

namespace Popcorn.Linux;

#pragma warning disable CA1416

public class WebviewGtk
{
    private static string AssemblyFolder = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;
    private static string WebPath = $"{AssemblyFolder}{Path.DirectorySeparatorChar}web{Path.DirectorySeparatorChar}"; //react bundle

    public static WebView CreateWebView()
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
        webView.SetSettings(webViewSettings);

        var ucm = webView.GetUserContentManager();
        var wsAddress = 1111;
        
        //Make webkit object persistent, not sure if it's a good idea, but it works
        ucm.AddScript(WebKit.UserScript.New(
            source: @"(function(globalContext) {
                        globalContext.window.wsAddress = " + $"'{wsAddress}';"
                    + @"})(this)",
            injectedFrames: WebKit.UserContentInjectedFrames.AllFrames,
            injectionTime: WebKit.UserScriptInjectionTime.Start,
            allowList: null,
            blockList: null)
        );
        
        //webView.LoadUri("http://localhost:3000");
        var webContext = webView.GetContext();
        webContext.RegisterUriScheme("app.local", (request) => HandleUriScheme(request));

        // Load initial file via custom scheme
        webView.LoadUri("app.local://index-linux.html");

        return webView;
    }
    
    private static void HandleUriScheme(URISchemeRequest request)
    {
        var requestedPath = request.GetUri().Replace("app.local://", "");

        if (requestedPath.StartsWith("null")) //Debugging .map files start with null for some reason
        {
            requestedPath = requestedPath.Substring(5);
        }
        if (requestedPath.StartsWith('/'))
        {
            requestedPath = requestedPath.Substring(1);
        }
        var filePath = Path.Combine(WebPath, requestedPath);

        // Read the file and respond
        var fileBytes = System.IO.File.ReadAllBytes(filePath);
        var mimeType = GuessMimeType(filePath);
            
        var headers = MessageHeaders.New(MessageHeadersType.Response);
        headers.SetContentLength(fileBytes.Length);
        // Disable local caching. This will prevent user scripts from executing correctly.
        headers.Append("Cache-Control", "no-cache, max-age=0, must-revalidate, no-store");
        headers.Append("Access-Control-Allow-Origin", "*");

        using var stream = MemoryInputStream.NewFromBytes(GLib.Bytes.New(fileBytes));
        var response = URISchemeResponse.New(stream, fileBytes.Length);
        response.SetContentType(mimeType);
        response.SetHttpHeaders(headers);
        request.FinishWithResponse(response);
    }

    private static string GuessMimeType(string filePath)
    {
        return filePath.EndsWith(".html") ? "text/html" :
            filePath.EndsWith(".css") ? "text/css" :
            filePath.EndsWith(".js") ? "application/javascript" :
            filePath.EndsWith(".png") ? "image/png" :
            filePath.EndsWith(".jpg") ? "image/jpeg" :
            "text/plain"; // Default to text
    }
}

#pragma warning enable CA1416
