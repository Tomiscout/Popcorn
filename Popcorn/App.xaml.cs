using Popcorn.PlayerServer;

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
        ServerPipeManager.GracefullDispose();
    }
}
