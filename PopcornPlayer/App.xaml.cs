using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Serilog;
using Popcorn.Shared.Utils;
using PopcornPlayer.Core;
using PopcornPlayer.Pipes;
using PopcornPlayer.Services;
using PopcornPlayer.Shared;

namespace PopcornPlayer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static bool _isShuttingDown = false;
        private static object _shutdownLock = new object();

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
                .WriteTo.File(Path.Combine(DirUtils.DataFolder, "logs\\PopcornPlayer.txt"), rollingInterval: RollingInterval.Day)
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
            DispatcherUnhandledException += (s, e) =>
            {
                Log.Error(e.Exception, "An unhandled exception occurred within the WPF dispatcher.");
                GracefulShutdown();
            };

            FFmpegWrapper.ExePath = "ffmpeg"; //TODO proper linking
            ThumbnailService.Initialize();
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            GracefulShutdown();
            base.OnExit(e);
        }

        public static void GracefulShutdown()
        {
            lock (_shutdownLock) // This could be executed from multiple locations, but ran only once
            {
                if (_isShuttingDown) return;
                _isShuttingDown = true;
            }

            PipeManager.Dispose();
            PlayerCore.Dispose();
            Log.CloseAndFlush();
        }
    }
}
