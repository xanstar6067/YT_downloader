namespace YT_downloader.Services;

public enum YtDlpErrorKind
{
    Unknown,
    ToolMissing,
    InvalidUrl,
    Network,
    BotVerification,
    VideoUnavailable,
    FfmpegMissing,
    AlreadyRunning
}

public sealed class YtDlpException(
    YtDlpErrorKind kind,
    string message,
    string? details = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public YtDlpErrorKind Kind { get; } = kind;

    public string Details { get; } = details ?? string.Empty;
}
