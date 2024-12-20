using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using System;
using Blazored.Modal;
using Popcorn.Shared.Linux;
using PopcornPlayer.Shared;
using FluentMigrator.Runner;
using MudBlazor.Services;
using Popcorn.Shared.Utils;
using Popcorn.Persistence;
using Serilog;
using Popcorn.WebView;
using Popcorn.Domain.Interfaces;
using Popcorn.Linux;
using Popcorn.PlayerServer;
using Popcorn.Shared.Interfaces;

namespace Popcorn.Photino;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.Debug()
            //.WriteTo.File(Path.Combine(DirUtils.DataFolder, $"logs{Path.DirectorySeparatorChar}PopcornPlayer.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
        
        PhotinoBlazorAppBuilder appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

        appBuilder.Services.AddLogging(builder => builder.AddSerilog());

        appBuilder.Services.AddMudServices();
        appBuilder.Services.AddBlazoredModal();

        appBuilder.Services.AddPopcornServices();
        appBuilder.Services.AddPopcornDatabase();

        appBuilder.Services.AddSingleton<IPlayerService, LinuxPlayerService>();
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
                
        app.MainWindow.SetContextMenuEnabled(true);
        app.MainWindow.SetDevToolsEnabled(true);
        app.MainWindow.SetIconFile("favicon.ico");
        app.MainWindow.SetTitle("Popcorn");
        app.MainWindow.SetUseOsDefaultSize(false);
        app.MainWindow.SetSize(1680, 1050);
        app.MainWindow.SetUseOsDefaultLocation(false);
        app.MainWindow.SetTop(0);
        app.MainWindow.SetLeft(0);
        app.MainWindow.SetLogVerbosity(-1);

        AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
        {
            Dispose();
            
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
        Dispose();
    }

    private static void Dispose()
    {
        //PlayerSocketManager.Dispose();
    }
}
