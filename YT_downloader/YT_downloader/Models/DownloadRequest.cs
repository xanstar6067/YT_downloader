namespace YT_downloader.Models;

public sealed record DownloadRequest(
    string Url,
    string OutputDirectory,
    DownloadMode Mode,
    string MaximumResolution,
    string? AudioFormatId,
    bool DownloadPlaylist = false);
