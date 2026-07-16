namespace YT_downloader.Models;

public sealed record ToolAvailability(
    bool YtDlpExists,
    bool FfmpegExists,
    bool FfprobeExists,
    bool JavaScriptRuntimeExists)
{
    public bool AllAvailable =>
        YtDlpExists && FfmpegExists && FfprobeExists && JavaScriptRuntimeExists;
}
