using System.Globalization;

namespace YT_downloader.Services;

internal sealed record YtDlpDownloadPlan(
    string MediaId,
    IReadOnlyList<YtDlpPlannedFormat> Formats,
    int? PlaylistIndex,
    int? PlaylistCount);

internal sealed record YtDlpPlannedFormat(string FormatId, long? FileSize);

internal static class YtDlpDownloadPlanParser
{
    private const string TemplatePrefix = "download-plan:";

    public static bool TryParse(string? line, out YtDlpDownloadPlan plan)
    {
        plan = new YtDlpDownloadPlan(string.Empty, [], null, null);
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmedLine = line.Trim();
        if (!trimmedLine.StartsWith(TemplatePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = trimmedLine[TemplatePrefix.Length..]
            .Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length != 9 || IsUnknown(parts[0]))
        {
            return false;
        }

        var formats = new List<YtDlpPlannedFormat>(2);
        AddFormat(formats, parts[3], parts[4]);
        AddFormat(formats, parts[5], parts[6]);
        if (formats.Count == 0)
        {
            AddFormat(formats, parts[1], parts[2]);
        }

        if (formats.Count == 0)
        {
            return false;
        }

        plan = new YtDlpDownloadPlan(
            parts[0],
            formats,
            ParseNullableInt(parts[7]),
            ParseNullableInt(parts[8]));
        return true;
    }

    private static void AddFormat(List<YtDlpPlannedFormat> formats, string formatId, string fileSize)
    {
        if (IsUnknown(formatId)
            || formats.Any(format => string.Equals(format.FormatId, formatId, StringComparison.Ordinal)))
        {
            return;
        }

        formats.Add(new YtDlpPlannedFormat(formatId, ParseNullableLong(fileSize)));
    }

    private static int? ParseNullableInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
        && result > 0
            ? result
            : null;

    private static long? ParseNullableLong(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
        && result > 0
            ? result
            : null;

    private static bool IsUnknown(string value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Equals("NA", StringComparison.OrdinalIgnoreCase)
        || value.Equals("N/A", StringComparison.OrdinalIgnoreCase)
        || value.Equals("none", StringComparison.OrdinalIgnoreCase);
}
