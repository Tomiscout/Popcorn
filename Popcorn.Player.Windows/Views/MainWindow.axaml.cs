using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Popcorn.Player.Windows.Views;
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnWindowOpened(object sender, EventArgs e)
    {
    }
}