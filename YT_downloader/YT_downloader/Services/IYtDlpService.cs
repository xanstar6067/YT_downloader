using YT_downloader.Models;

namespace YT_downloader.Services;

public interface IYtDlpService
{
    ToolAvailability GetToolAvailability();

    Task<VideoInfo> AnalyzeAsync(
        string url,
        IProgress<string>? log,
        CancellationToken cancellationToken);

    Task DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? log,
        CancellationToken cancellationToken);

    Task<string> UpdateAsync(IProgress<string>? log, CancellationToken cancellationToken);
}
