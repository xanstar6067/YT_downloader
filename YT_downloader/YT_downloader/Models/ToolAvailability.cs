namespace YT_downloader.Models;

public sealed record ToolAvailability(bool YtDlpExists, bool FfmpegExists, bool FfprobeExists)
{
    public bool AllAvailable => YtDlpExists && FfmpegExists && FfprobeExists;
}
