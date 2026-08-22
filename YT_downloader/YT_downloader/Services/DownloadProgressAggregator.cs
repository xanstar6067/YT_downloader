using YT_downloader.Models;

namespace YT_downloader.Services;

internal sealed class DownloadProgressAggregator(DownloadMode mode)
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ItemState> _items = new(StringComparer.Ordinal);

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
        private readonly List<string> _fallbackFormatOrder = [];
        private double? _lastPercent;

        public double? Aggregate(DownloadProgress progress, DownloadMode mode)
        {
            var effectivePercent = GetEffectiveStreamPercent(progress);
            if (!effectivePercent.HasValue)
            {
                return _lastPercent;
            }

            if (string.IsNullOrWhiteSpace(progress.FormatId))
            {
                return KeepMonotonic(effectivePercent.Value);
            }

            var rawFraction = Math.Clamp(effectivePercent.Value / 100, 0, 1);
            return KeepMonotonic(CalculateFallbackPercent(progress, mode, rawFraction));
        }

        private static double? GetEffectiveStreamPercent(DownloadProgress progress)
        {
            if (progress.FragmentIndex.HasValue && progress.FragmentCount is > 0)
            {
                var fragmentIndex = Math.Clamp(
                    progress.FragmentIndex.Value,
                    0,
                    progress.FragmentCount.Value);
                return fragmentIndex * 100d / progress.FragmentCount.Value;
            }

            return progress.Percent;
        }

        private double CalculateFallbackPercent(
            DownloadProgress progress,
            DownloadMode mode,
            double rawFraction)
        {
            if (mode != DownloadMode.Mp4Video)
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

    }
}
