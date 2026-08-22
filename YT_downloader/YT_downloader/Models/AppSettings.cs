namespace YT_downloader.Models;

public sealed class AppSettings
{
    public string SaveFolder { get; set; } = string.Empty;

    public DownloadMode Mode { get; set; } = DownloadMode.Mp4Video;

    public string MaximumResolution { get; set; } = "best";

    public bool DownloadPlaylist { get; set; }

    public bool IsLightTheme { get; set; }

    public BrowserCookieSource BrowserCookieSource { get; set; }
}
