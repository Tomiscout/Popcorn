using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PhotinoNET;
using Popcorn.Player.Views;

namespace Popcorn.Player;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void RegisterServices()
    {
        base.RegisterServices();
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        LinuxImports.Native.XInitThreads();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        
        //await InitializePhotinoHostAsync();

        base.OnFrameworkInitializationCompleted();
    }
    
    private static async Task InitializePhotinoHostAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = new PhotinoWindow()
                .SetTitle("Photino Embedded")
                .SetResizable(true)
                .SetSize(1024, 800)
                .Load("wwwroot/index.html");

            window.WaitForClose();
        });
    }
}