using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using System;
using Popcorn.Core;

namespace Popcorn.Photino;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        PhotinoBlazorAppBuilder appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

        appBuilder.Services.AddLogging();
        //         appBuilder.Services.AddLogging(builder => builder.AddSerilog());


        //appBuilder.Services.AddServices();
        //appBuilder.Services.AddWebViewServices();
        appBuilder.Services.AddPopcornServices();

        // register root component and selector
        appBuilder.RootComponents.Add<Popcorn.WebView.Main>("app");
        appBuilder.RootComponents.Add<HeadOutlet>("head::after");

        PhotinoBlazorApp app = appBuilder.Build();

        // customize window
        if (!OperatingSystem.IsLinux()) // TODO: find out why this works in Photino sample
            app.MainWindow.SetIconFile("favicon.ico");
        app.MainWindow.SetTitle("Popcorn");
        app.MainWindow.SetUseOsDefaultSize(false);
        app.MainWindow.SetSize(1680, 1050);
        app.MainWindow.SetUseOsDefaultLocation(false);
        app.MainWindow.SetTop(0);
        app.MainWindow.SetLeft(0);
        app.MainWindow.SetDevToolsEnabled(true);
        app.MainWindow.SetLogVerbosity(1);

        AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
        {
            try
            {
                string? message = error.ExceptionObject.ToString();

                System.Diagnostics.Debug.WriteLine(message);

                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ididit", "Error.log");
                System.IO.File.WriteAllText(path, message);

                app.MainWindow.ShowMessage("Error", message);
            }
            catch
            {
            }
        };

        app.Run();
    }
}
