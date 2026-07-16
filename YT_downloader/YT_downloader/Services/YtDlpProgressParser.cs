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
        if (parts.Length != 4)
        {
            return false;
        }

        var percentText = parts[0].Trim().TrimEnd('%').Trim();
        double? percent = TryParsePercent(percentText, out var parsedPercent) ? parsedPercent : null;

        progress = new DownloadProgress(
            percent,
            Normalize(parts[1]),
            Normalize(parts[2]),
            Normalize(parts[3]));
        return true;
    }

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
}
