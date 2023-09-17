using System;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Serilog;
using VideoPlayer.Pipes;

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
                .WriteTo.File("logs\\VideoPlayer.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

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
