using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Serilog;
using VideoBlazor.Shared.Utils;
using VideoPlayer.Pipes;
using VideoPlayer.Services;
using VideoPlayer.Shared;

namespace VideoPlayer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IConfiguration Config { get; private set; }

        public App()
        {
            Config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.Debug()
                .WriteTo.File(Path.Combine(DirUtils.DataFolder, "logs\\VideoPlayer.txt"), rollingInterval: RollingInterval.Day)
                .CreateLogger();
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var exception = e.ExceptionObject as Exception;
                Log.Fatal(exception, "An unhandled exception occurred.");
                Log.CloseAndFlush();
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log.Error(e.Exception, "An unobserved task exception occurred.");
                e.SetObserved(); // This prevents the exception from terminating the application.
            };
            this.DispatcherUnhandledException += (s, e) =>
            {
                Log.Error(e.Exception, "An unhandled exception occurred within the WPF dispatcher.");
                e.Handled = true; // This prevents the exception from terminating the application.
            };

            FFmpegWrapper.ExePath = "ffmpeg"; //TODO proper linking
            ThumbnailService.Initialize();
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            PipeManager.Dispose();
            PlayerCore.Dispose();
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
