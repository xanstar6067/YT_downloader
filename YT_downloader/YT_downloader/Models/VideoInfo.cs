namespace YT_downloader.Models;

public sealed record VideoInfo(
    string Title,
    string? ThumbnailUrl,
    string? DurationText,
    IReadOnlyList<AudioTrackInfo> AudioTracks);
