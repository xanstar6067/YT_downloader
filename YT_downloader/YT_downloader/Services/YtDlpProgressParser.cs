using System.Globalization;
using System.Text.RegularExpressions;
using YT_downloader.Models;

namespace YT_downloader.Services;

public static class YtDlpProgressParser
{
    private const string TemplatePrefix = "download:";

    private static readonly Regex StandardProgressRegex = new(
        @"^\[download\]\s+(?<percent>\d+(?:[.,]\d+)?)%\s+of(?:\s+~)?\s*(?<size>\S+)(?:\s+at\s+(?<speed>\S+))?(?:\s+ETA\s+(?<eta>\S+))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string? line, out DownloadProgress progress)
    {
        progress = new DownloadProgress(null, "—", "—", "—");

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmedLine = line.Trim();
        if (trimmedLine.StartsWith(TemplatePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseTemplate(trimmedLine[TemplatePrefix.Length..], out progress);
        }

        var match = StandardProgressRegex.Match(trimmedLine);
        if (!match.Success || !TryParsePercent(match.Groups["percent"].Value, out var percent))
        {
            return false;
        }

        progress = new DownloadProgress(
            percent,
            Normalize(match.Groups["speed"].Value),
            Normalize(match.Groups["size"].Value),
            Normalize(match.Groups["eta"].Value));
        return true;
    }

    private static bool TryParseTemplate(string payload, out DownloadProgress progress)
    {
        progress = new DownloadProgress(null, "—", "—", "—");
        var parts = payload.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length == 12)
        {
            return TryParseDetailedTemplate(parts, out progress);
        }

        if (parts.Length is not 4 and not 6)
        {
            return false;
        }

        var percentText = parts[0].Trim().TrimEnd('%').Trim();
        double? percent = TryParsePercent(percentText, out var parsedPercent) ? parsedPercent : null;
        var playlistIndex = parts.Length == 6 ? ParseNullableInt(parts[4]) : null;
        var playlistCount = parts.Length == 6 ? ParseNullableInt(parts[5]) : null;

        progress = new DownloadProgress(
            percent,
            Normalize(parts[1]),
            Normalize(parts[2]),
            Normalize(parts[3]),
            PlaylistIndex: playlistIndex,
            PlaylistCount: playlistCount);
        return true;
    }

    private static bool TryParseDetailedTemplate(
        IReadOnlyList<string> parts,
        out DownloadProgress progress)
    {
        var percentText = parts[4].Trim().TrimEnd('%').Trim();
        double? percent = TryParsePercent(percentText, out var parsedPercent) ? parsedPercent : null;

        progress = new DownloadProgress(
            percent,
            Normalize(parts[5]),
            Normalize(parts[6]),
            Normalize(parts[7]),
            PlaylistIndex: ParseNullableInt(parts[8]),
            PlaylistCount: ParseNullableInt(parts[9]),
            MediaId: NormalizeOptional(parts[0]),
            FormatId: NormalizeOptional(parts[1]),
            DownloadedBytes: ParseNullableLong(parts[2]),
            TotalBytes: ParseNullableLong(parts[3]),
            VideoCodec: NormalizeOptional(parts[10]),
            AudioCodec: NormalizeOptional(parts[11]));
        return true;
    }

    private static int? ParseNullableInt(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            && result > 0
                ? result
                : null;

    private static long? ParseNullableLong(string value) =>
        long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            && result >= 0
                ? result
                : null;

    private static bool TryParsePercent(string value, out double percent)
    {
        var normalized = value.Replace(',', '.');
        return double.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out percent);
    }

    private static string Normalize(string value)
    {
        var normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            || normalized.Equals("NA", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                ? "—"
                : normalized;
    }

    private static string? NormalizeOptional(string value)
    {
        var normalized = Normalize(value);
        return normalized == "—" ? null : normalized;
    }
}
