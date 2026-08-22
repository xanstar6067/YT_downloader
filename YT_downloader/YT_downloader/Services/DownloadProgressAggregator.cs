using YT_downloader.Models;

namespace YT_downloader.Services;

internal sealed class DownloadProgressAggregator(DownloadMode mode)
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ItemState> _items = new(StringComparer.Ordinal);

    public void RegisterPlan(YtDlpDownloadPlan plan)
    {
        lock (_sync)
        {
            GetOrCreateItem(BuildItemKey(plan.MediaId, plan.PlaylistIndex))
                .SetPlan(plan.Formats);
        }
    }

    public DownloadProgress Aggregate(DownloadProgress progress)
    {
        lock (_sync)
        {
            var item = GetOrCreateItem(BuildItemKey(progress.MediaId, progress.PlaylistIndex));
            var percent = item.Aggregate(progress, mode);
            return progress with
            {
                Percent = percent,
                Status = GetStatus(progress)
            };
        }
    }

    private ItemState GetOrCreateItem(string key)
    {
        if (_items.TryGetValue(key, out var item))
        {
            return item;
        }

        item = new ItemState();
        _items.Add(key, item);
        return item;
    }

    private static string BuildItemKey(string? mediaId, int? playlistIndex) =>
        $"{playlistIndex?.ToString() ?? "-"}:{mediaId ?? "current"}";

    private static string GetStatus(DownloadProgress progress)
    {
        var hasVideo = IsCodecPresent(progress.VideoCodec);
        var hasAudio = IsCodecPresent(progress.AudioCodec);
        return (hasVideo, hasAudio) switch
        {
            (true, false) => "Загрузка видео",
            (false, true) => "Загрузка аудио",
            _ => progress.Status
        };
    }

    private static bool IsCodecPresent(string? codec) =>
        !string.IsNullOrWhiteSpace(codec)
        && !codec.Equals("none", StringComparison.OrdinalIgnoreCase)
        && !codec.Equals("NA", StringComparison.OrdinalIgnoreCase);

    private sealed class ItemState
    {
        private readonly Dictionary<string, FormatState> _formats = new(StringComparer.Ordinal);
        private readonly List<string> _fallbackFormatOrder = [];
        private bool _hasExplicitPlan;
        private double? _lastPercent;

        public void SetPlan(IReadOnlyList<YtDlpPlannedFormat> formats)
        {
            _hasExplicitPlan = true;
            foreach (var format in formats)
            {
                if (_formats.TryGetValue(format.FormatId, out var state))
                {
                    state.FileSize = format.FileSize ?? state.FileSize;
                }
                else
                {
                    _formats.Add(format.FormatId, new FormatState(format.FileSize));
                }
            }
        }

        public double? Aggregate(DownloadProgress progress, DownloadMode mode)
        {
            if (!progress.Percent.HasValue)
            {
                return _lastPercent;
            }

            var rawFraction = Math.Clamp(progress.Percent.Value / 100, 0, 1);
            if (string.IsNullOrWhiteSpace(progress.FormatId))
            {
                return KeepMonotonic(progress.Percent.Value);
            }

            var formatId = progress.FormatId;
            if (!_formats.TryGetValue(formatId, out var format))
            {
                format = new FormatState(progress.TotalBytes);
                _formats.Add(formatId, format);
            }

            format.FileSize ??= progress.TotalBytes;
            format.Fraction = Math.Max(format.Fraction, rawFraction);

            double aggregatedPercent;
            if (_hasExplicitPlan)
            {
                aggregatedPercent = CalculatePlannedFraction() * 100;
            }
            else
            {
                aggregatedPercent = CalculateFallbackPercent(progress, mode, rawFraction);
            }

            return KeepMonotonic(aggregatedPercent);
        }

        private double CalculatePlannedFraction()
        {
            var plannedFormats = _formats.Values.ToArray();
            if (plannedFormats.Length == 0)
            {
                return 0;
            }

            if (plannedFormats.All(format => format.FileSize is > 0))
            {
                var totalSize = plannedFormats.Sum(format => (double)format.FileSize!.Value);
                return plannedFormats.Sum(format => format.FileSize!.Value * format.Fraction) / totalSize;
            }

            return plannedFormats.Average(format => format.Fraction);
        }

        private double CalculateFallbackPercent(
            DownloadProgress progress,
            DownloadMode mode,
            double rawFraction)
        {
            var isSeparateMediaStream = IsCodecPresent(progress.VideoCodec)
                                        ^ IsCodecPresent(progress.AudioCodec);
            if (mode != DownloadMode.Mp4Video || !isSeparateMediaStream)
            {
                return rawFraction * 100;
            }

            if (!_fallbackFormatOrder.Contains(progress.FormatId!, StringComparer.Ordinal))
            {
                _fallbackFormatOrder.Add(progress.FormatId!);
            }

            var stageIndex = Math.Min(_fallbackFormatOrder.IndexOf(progress.FormatId!), 1);
            return (stageIndex + rawFraction) * 50;
        }

        private double KeepMonotonic(double percent)
        {
            var normalized = Math.Clamp(percent, 0, 100);
            _lastPercent = Math.Max(_lastPercent ?? 0, normalized);
            return _lastPercent.Value;
        }

        private sealed class FormatState(long? fileSize)
        {
            public long? FileSize { get; set; } = fileSize;

            public double Fraction { get; set; }
        }
    }
}
