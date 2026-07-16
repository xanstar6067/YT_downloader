using System.Globalization;
using System.Text.Json;
using YT_downloader.Models;

namespace YT_downloader.Services;

public static class YtDlpMetadataParser
{
    public static VideoInfo Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var title = root.TryGetProperty("title", out var titleElement)
            ? titleElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new JsonException("В ответе yt-dlp отсутствует название видео.");
        }

        return new VideoInfo(
            title,
            GetThumbnailUrl(root),
            GetDurationText(root),
            GetAudioTracks(root));
    }

    private static IReadOnlyList<AudioTrackInfo> GetAudioTracks(JsonElement root)
    {
        if (!root.TryGetProperty("formats", out var formatsElement)
            || formatsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candidates = new List<AudioFormatCandidate>();
        foreach (var format in formatsElement.EnumerateArray())
        {
            var formatId = GetString(format, "format_id");
            var language = GetString(format, "language");
            var videoCodec = GetString(format, "vcodec");
            var audioCodec = GetString(format, "acodec");

            if (string.IsNullOrWhiteSpace(formatId)
                || string.IsNullOrWhiteSpace(language)
                || !string.Equals(videoCodec, "none", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(audioCodec)
                || string.Equals(audioCodec, "none", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var extension = GetString(format, "ext");
            var bitrate = GetDouble(format, "abr") ?? GetDouble(format, "tbr") ?? 0;
            var languagePreference = GetDouble(format, "language_preference") ?? 0;
            var isMp4Compatible = string.Equals(extension, "m4a", StringComparison.OrdinalIgnoreCase)
                || audioCodec.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase)
                || audioCodec.StartsWith("aac", StringComparison.OrdinalIgnoreCase);

            candidates.Add(new AudioFormatCandidate(
                formatId,
                language,
                bitrate,
                languagePreference,
                isMp4Compatible));
        }

        return candidates
            .GroupBy(candidate => candidate.LanguageCode, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var best = group
                    .OrderByDescending(candidate => candidate.Bitrate)
                    .First();
                var mp4 = group
                    .Where(candidate => candidate.IsMp4Compatible)
                    .OrderByDescending(candidate => candidate.Bitrate)
                    .FirstOrDefault();
                var isOriginal = group.Any(candidate => candidate.LanguagePreference > 0);

                return new AudioTrackInfo(
                    best.FormatId,
                    mp4?.FormatId,
                    BuildDisplayName(group.Key, isOriginal),
                    group.Key);
            })
            .OrderBy(track => track.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string BuildDisplayName(string languageCode, bool isOriginal)
    {
        string languageName;
        try
        {
            languageName = CultureInfo.GetCultureInfo(languageCode).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            languageName = languageCode;
        }

        if (languageName.Length > 0)
        {
            languageName = char.ToUpper(languageName[0], CultureInfo.CurrentUICulture) + languageName[1..];
        }

        var originalLabel = isOriginal ? " — оригинал" : string.Empty;
        return $"{languageName} ({languageCode}){originalLabel}";
    }

    private static string? GetThumbnailUrl(JsonElement root)
    {
        if (root.TryGetProperty("thumbnail", out var thumbnailElement))
        {
            return thumbnailElement.GetString();
        }

        if (!root.TryGetProperty("thumbnails", out var thumbnailsElement)
            || thumbnailsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? lastUrl = null;
        foreach (var thumbnail in thumbnailsElement.EnumerateArray())
        {
            if (thumbnail.TryGetProperty("url", out var urlElement))
            {
                lastUrl = urlElement.GetString() ?? lastUrl;
            }
        }

        return lastUrl;
    }

    private static string? GetDurationText(JsonElement root)
    {
        if (root.TryGetProperty("duration_string", out var durationString))
        {
            return durationString.GetString();
        }

        if (root.TryGetProperty("duration", out var duration)
            && duration.TryGetDouble(out var seconds)
            && seconds >= 0)
        {
            return TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static double? GetDouble(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.TryGetDouble(out var value)
            ? value
            : null;

    private sealed record AudioFormatCandidate(
        string FormatId,
        string LanguageCode,
        double Bitrate,
        double LanguagePreference,
        bool IsMp4Compatible);
}
