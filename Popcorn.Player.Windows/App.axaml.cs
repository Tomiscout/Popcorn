using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Popcorn.Player.Windows.Views;

namespace Popcorn.Player.Windows;

public partial class App : Application
{
    public static double CurrentScalingFactor { get; private set; } = 1.0;

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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            CurrentScalingFactor = desktop.MainWindow.RenderScaling;
            desktop.MainWindow.ScalingChanged += (object sender, EventArgs e) =>
            {
                int a = 0;
            };
        }
        
        base.OnFrameworkInitializationCompleted();
    }
}