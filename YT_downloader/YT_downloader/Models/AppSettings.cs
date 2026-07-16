namespace YT_downloader.Models;

public sealed class AppSettings
{
    public string SaveFolder { get; set; } = string.Empty;

    public DownloadMode Mode { get; set; } = DownloadMode.Mp4Video;

    public string MaximumResolution { get; set; } = "best";
}
