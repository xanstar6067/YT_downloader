using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace YT_downloader.Services;

public sealed class WpfUserInteractionService : IUserInteractionService
{
    public string? GetClipboardText() => Clipboard.ContainsText() ? Clipboard.GetText().Trim() : null;

    public string? SelectFolder(string initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для сохранения",
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : null,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public void ShowError(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInformation(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
