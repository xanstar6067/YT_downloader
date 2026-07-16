namespace YT_downloader.Models;

public sealed record DownloadProgress(
    double? Percent,
    string Speed,
    string FileSize,
    string RemainingTime,
    string Status = "Загрузка");
