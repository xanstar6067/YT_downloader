namespace YT_downloader.Models;

public sealed record LogEntry(DateTime Timestamp, string Message, LogSeverity Severity);
