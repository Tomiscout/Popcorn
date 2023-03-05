using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using VideoPlayer.Pipes;

namespace WebSiteRatings
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
            Console.WriteLine("Video player started");
            Task.Run(async () => await PipeManager.Initialize(e.Args));
        }
    }
}
