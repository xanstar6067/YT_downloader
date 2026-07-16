namespace YT_downloader.Services;

public interface IUserInteractionService
{
    string? GetClipboardText();

    string? SelectFolder(string initialDirectory);

    void ShowError(string title, string message);

    void ShowInformation(string title, string message);
}
