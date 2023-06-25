using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
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
        
        private void Window_Destroying(object sender, EventArgs e)
        {
            PipeManager.Dispose();
        }
    }
}
