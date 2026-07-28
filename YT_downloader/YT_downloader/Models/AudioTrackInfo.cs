namespace YT_downloader.Models;

public sealed record AudioTrackInfo(
    string? BestFormatId,
    string? Mp4FormatId,
    string DisplayName,
    string? LanguageCode)
{
    public override string ToString() => DisplayName;
}
