using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using YT_downloader.Models;
using YT_downloader.ViewModels;

namespace YT_downloader.Views;

public partial class MainWindow : Window
{
    private const double AutoScrollTolerance = 24;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PreviewCover.SizeChanged += (_, _) =>
            PreviewCover.Clip = new RectangleGeometry(new Rect(0, 0, PreviewCover.ActualWidth, PreviewCover.ActualHeight), 6, 6);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldViewModel)
        {
            oldViewModel.LogEntries.CollectionChanged -= OnLogEntriesChanged;
        }

        LogRichTextBox.Document.Blocks.Clear();

        if (e.NewValue is MainViewModel newViewModel)
        {
            foreach (var entry in newViewModel.LogEntries)
            {
                LogRichTextBox.Document.Blocks.Add(CreateParagraph(entry));
            }

            newViewModel.LogEntries.CollectionChanged += OnLogEntriesChanged;
            LogRichTextBox.ScrollToEnd();
        }
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var wasNearBottom = IsScrolledNearBottom();

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (LogEntry entry in e.NewItems!)
                {
                    LogRichTextBox.Document.Blocks.Add(CreateParagraph(entry));
                }

                break;
            case NotifyCollectionChangedAction.Remove:
                for (var i = 0; i < e.OldItems!.Count && LogRichTextBox.Document.Blocks.FirstBlock is { } first; i++)
                {
                    LogRichTextBox.Document.Blocks.Remove(first);
                }

                break;
            case NotifyCollectionChangedAction.Reset:
                LogRichTextBox.Document.Blocks.Clear();
                break;
        }

        if (wasNearBottom)
        {
            Dispatcher.BeginInvoke(() => LogRichTextBox.ScrollToEnd(), DispatcherPriority.Background);
        }
    }

    private bool IsScrolledNearBottom() =>
        LogRichTextBox.ExtentHeight - LogRichTextBox.ViewportHeight <= AutoScrollTolerance
        || LogRichTextBox.VerticalOffset >= LogRichTextBox.ExtentHeight - LogRichTextBox.ViewportHeight - AutoScrollTolerance;

    private static Paragraph CreateParagraph(LogEntry entry)
    {
        var run = new Run($"[{entry.Timestamp:HH:mm:ss}] {entry.Message}")
        {
            Foreground = ResolveBrush(entry.Severity)
        };
        return new Paragraph(run) { Margin = new Thickness(0, 0, 0, 2) };
    }

    private static Brush ResolveBrush(LogSeverity severity)
    {
        var resourceKey = severity switch
        {
            LogSeverity.Error => "LogErrorBrush",
            LogSeverity.Warning => "LogWarningBrush",
            LogSeverity.Success => "LogSuccessBrush",
            _ => "TextBrush"
        };

        return Application.Current.TryFindResource(resourceKey) as Brush ?? Brushes.Gray;
    }
}
