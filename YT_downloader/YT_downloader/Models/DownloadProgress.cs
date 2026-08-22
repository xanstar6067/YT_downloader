namespace YT_downloader.Models;

public sealed record DownloadProgress(
    double? Percent,
    string Speed,
    string FileSize,
    string RemainingTime,
    string Status = "Загрузка",
    int? PlaylistIndex = null,
    int? PlaylistCount = null,
    string? MediaId = null,
    string? FormatId = null,
    long? DownloadedBytes = null,
    long? TotalBytes = null,
    string? VideoCodec = null,
    string? AudioCodec = null,
    int? FragmentIndex = null,
    int? FragmentCount = null);
