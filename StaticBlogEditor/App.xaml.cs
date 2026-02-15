using System;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;

namespace StaticBlogEditor;
public partial class App : Application
{
    private SimpleApiServer? _server;
    
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 通常のウィンドウ生成処理...
        MainWindow window = new MainWindow();
        window.Activate();

        string serverUri = "http://localhost:30078/";
        _server = new SimpleApiServer(serverUri);
        window.EditorUI.Source = new Uri(serverUri);
        _server.Start();

        window.Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object? sender, WindowEventArgs e)
    {
        _server?.Dispose();
        _server = null;
    }
}