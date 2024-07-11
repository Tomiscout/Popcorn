using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using System;
using Popcorn.Core;
using PopcornPlayer.Shared;
using FluentMigrator.Runner;
using Popcorn.Shared.Utils;
using Popcorn.Domain.Interfaces;
using Serilog;
using Popcorn.Photino.Native;

namespace Popcorn.Photino;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        PhotinoBlazorAppBuilder appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

        appBuilder.Services.AddLogging(builder => builder.AddSerilog());

        appBuilder.Services.AddPopcornServices();
        appBuilder.Services.AddSingleton<INativeFolderService, LinuxFolderService>();
        appBuilder.Services.AddSingleton<INativeFileAssosiation, LinuxFileAssociation>();
        appBuilder.Services.AddSingleton<INativeProcessRunner, LinuxProcessRunner>();

        // register root component and selector
        appBuilder.RootComponents.Add<Popcorn.WebView.Main>("app");
        appBuilder.RootComponents.Add<HeadOutlet>("head::after");

        FFmpegWrapper.ExePath = "ffmpeg"; //TODO proper linking
        DirUtils.EnsureApplicationFolder();

        PhotinoBlazorApp app = appBuilder.Build();

        var runner = app.Services.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();

        // customize window
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

                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Popcorn", "Error.log");
                System.IO.File.WriteAllText(path, message);

                app.MainWindow.ShowMessage("Error", message);
            }
            catch (Exception e)
            {
            }
        };

        app.Run();
    }
}
