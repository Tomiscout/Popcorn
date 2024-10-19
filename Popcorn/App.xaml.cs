using Popcorn.PlayerServer;
using Popcorn.PlayerServer.Windows;

namespace Popcorn;

public partial class App : Application
{
	public App()
	{
        InitializeComponent();

		MainPage = new MainPage();
	}

    //TODO tray icon
    private void Window_Destroying(object sender, EventArgs e)
    {
        ServerPipeManager.Dispose();
        FileServer.FileServer.Dispose();
    }

    protected override Window CreateWindow(IActivationState activationState)
    {
        var window = base.CreateWindow(activationState);
        if (window != null)
        {
            window.Title = "Popcorn";
        }

        return window;
    }
}
