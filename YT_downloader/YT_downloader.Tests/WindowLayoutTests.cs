using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using YT_downloader.Views;
using YT_downloader.Models;
using YT_downloader.Services;
using YT_downloader.ViewModels;

namespace YT_downloader.Tests;

[TestClass]
public sealed class WindowLayoutTests
{
    [TestMethod]
    public void CompactLayouts_KeepActionsVisibleAndAllSettingsReachable()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                var settings = new MemorySettingsService();
                var viewModel = new MainViewModel(new YtDlpService(Path.GetTempPath()), settings,
                    new WpfUserInteractionService(), new WpfThemeService());
                var placement = new WindowPlacement(100, 80, 1100, 800, true);
                viewModel.SaveWindowPlacement(placement);
                viewModel.SelectedResolution = "720";
                Assert.AreEqual(placement, settings.Settings.WindowPlacement, "Other settings must preserve window placement");
                foreach (var (width, height, scale) in new[]
                {
                    (920.0, 600.0, 1.0),
                    (1280.0, 688.0, 1.25),
                    (1066.0, 573.0, 1.5),
                    (800.0, 480.0, 2.0),
                    (780.0, 480.0, 1.0)
                })
                {
                    var window = new MainWindow { DataContext = viewModel };
                    var root = (Grid)window.Content;
                    root.Background = (Brush)app.FindResource("WindowBrush");
                    // Reserve native title bar/borders as well as the root's outer margins.
                    var size = new Size(width - 16, height - 39);
                    root.Measure(size);
                    root.Arrange(new Rect(size));
                    root.UpdateLayout();

                    var preview = (Border)window.FindName("PreviewCard");
                    var options = (Border)window.FindName("OptionsCard");
                    var progress = (Grid)window.FindName("ProgressCard");
                    var log = (Border)window.FindName("LogCard");
                    foreach (var element in new FrameworkElement[] { preview, options, progress, log })
                    {
                        var position = element.TranslatePoint(new Point(), root);
                        Assert.IsTrue(position.Y >= 0 && position.Y + element.ActualHeight <= root.ActualHeight + 1,
                            $"{width}×{height}: {element.Name} clipped");
                    }
                    Assert.IsGreaterThan(progress.TranslatePoint(new Point(), root).Y, log.TranslatePoint(new Point(), root).Y,
                        "The execution log must sit below progress");
                    Assert.IsGreaterThan(40.0, log.ActualHeight, $"{width}×{height}: log must keep usable height");
                    var url = (TextBox)window.FindName("UrlTextBox");
                    var playlist = (CheckBox)window.FindName("PlaylistCheckBox");
                    var urlCenter = url.TranslatePoint(new Point(0, url.ActualHeight / 2), root).Y;
                    var playlistCenter = playlist.TranslatePoint(new Point(0, playlist.ActualHeight / 2), root).Y;
                    Assert.IsLessThan(2.0, Math.Abs(urlCenter - playlistCenter), "Playlist choice must share the URL row");

                    foreach (var name in new[] { "DownloadButton", "CancelButton" })
                    {
                        var button = (Button)window.FindName(name);
                        Assert.IsTrue(preview.IsAncestorOf(button), "Download controls must live in the media card");
                        var position = button.TranslatePoint(new Point(), root);
                        Assert.IsTrue(position.X >= 0 && position.X + button.ActualWidth <= root.ActualWidth + 1
                            && position.Y >= 0 && position.Y + button.ActualHeight <= root.ActualHeight + 1,
                            $"{width}×{height}: {name} must be visible");
                    }

                    // Render offscreen for visual inspection; no application window is displayed.
                    root.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale),
                        (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var directory = Path.Combine(AppContext.BaseDirectory, "layout-previews");
                    Directory.CreateDirectory(directory);
                    using var stream = File.Create(Path.Combine(directory, $"window-{width}-{height}.png"));
                    encoder.Save(stream);
                    window.Close();
                }

                WindowPlacement? captured = null;
                var nativeWindow = new MainWindow();
                _ = new WindowPlacementService(nativeWindow, new WindowPlacement(80, 80, 900, 600, false), value => captured = value);
                new WindowInteropHelper(nativeWindow).EnsureHandle();
                nativeWindow.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                nativeWindow.Left += 20;
                nativeWindow.Width += 30;
                nativeWindow.Close();
                Assert.IsNotNull(captured, "Closing the native window must flush its updated bounds");
                Assert.IsFalse(captured.IsMaximized);
                Assert.IsGreaterThan(0, captured.Width);
                var normalBounds = captured;

                var maximizedWindow = new MainWindow();
                _ = new WindowPlacementService(maximizedWindow, captured with { IsMaximized = true }, value => captured = value);
                new WindowInteropHelper(maximizedWindow).EnsureHandle();
                maximizedWindow.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Assert.AreEqual(WindowState.Maximized, maximizedWindow.WindowState);
                maximizedWindow.WindowState = WindowState.Minimized;
                maximizedWindow.Close();
                Assert.IsTrue(captured.IsMaximized, "Closing while minimized must preserve the previous maximized state");
                Assert.AreEqual(normalBounds.Width, captured.Width, "Maximizing must preserve the normal window width");
                Assert.AreEqual(normalBounds.Height, captured.Height, "Maximizing must preserve the normal window height");
                viewModel.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Layout verification timed out");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class MemorySettingsService : ISettingsService
    {
        public AppSettings Settings { get; private set; } = new();
        public AppSettings Load() => Settings;
        public void Save(AppSettings settings) => Settings = settings;
    }
}
