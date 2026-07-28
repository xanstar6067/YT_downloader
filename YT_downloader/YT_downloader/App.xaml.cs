using System.Configuration;
using System.Windows;
using System.IO;
using YT_downloader.Services;
using YT_downloader.ViewModels;
using YT_downloader.Views;

namespace YT_downloader;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var assemblyDirectory = Path.GetDirectoryName(typeof(App).Assembly.Location);
        var applicationDirectory = string.IsNullOrWhiteSpace(assemblyDirectory)
            ? AppContext.BaseDirectory
            : assemblyDirectory;
        var toolsDirectory = Path.Combine(applicationDirectory, "Tools");
        var ytDlpService = new YtDlpService(toolsDirectory);
        var settingsService = new JsonSettingsService();
        var userInteractionService = new WpfUserInteractionService();
        var themeService = new WpfThemeService();

        _mainViewModel = new MainViewModel(
            ytDlpService,
            settingsService,
            userInteractionService,
            themeService);
        var mainWindow = new MainWindow { DataContext = _mainViewModel };

        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Shutdown();
        base.OnExit(e);
    }
}
