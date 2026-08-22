using YT_downloader.Models;
using YT_downloader.Services;

namespace YT_downloader.Tests;

[TestClass]
public sealed class DownloadProgressAggregatorTests
{
    [TestMethod]
    public void Aggregate_SeparateStreams_UsesByteWeightedMonotonicPercent()
    {
        var aggregator = new DownloadProgressAggregator(DownloadMode.Mp4Video);
        aggregator.RegisterPlan(new YtDlpDownloadPlan(
            "video-id",
            [new("video", 900), new("audio", 100)],
            null,
            null));

        var videoFinished = aggregator.Aggregate(CreateProgress(
            mediaId: "video-id",
            formatId: "video",
            percent: 100,
            videoCodec: "avc1",
            audioCodec: "none"));
        var audioStarted = aggregator.Aggregate(CreateProgress(
            mediaId: "video-id",
            formatId: "audio",
            percent: 20,
            videoCodec: "none",
            audioCodec: "mp4a"));

        Assert.AreEqual(90, videoFinished.Percent);
        Assert.AreEqual(92, audioStarted.Percent);
        Assert.AreEqual("Загрузка видео", videoFinished.Status);
        Assert.AreEqual("Загрузка аудио", audioStarted.Status);
    }

    [TestMethod]
    public void Aggregate_PercentEstimateMovesBackward_KeepsPreviousValue()
    {
        var aggregator = new DownloadProgressAggregator(DownloadMode.Mp3Audio);
        aggregator.RegisterPlan(new YtDlpDownloadPlan(
            "audio-id",
            [new("audio", 1000)],
            null,
            null));

        var first = aggregator.Aggregate(CreateProgress("audio-id", "audio", 60, "none", "opus"));
        var lowerEstimate = aggregator.Aggregate(CreateProgress("audio-id", "audio", 45, "none", "opus"));

        Assert.AreEqual(60, first.Percent);
        Assert.AreEqual(60, lowerEstimate.Percent);
    }

    [TestMethod]
    public void Aggregate_WithoutPlan_AllocatesHalfToEachSeparateMp4Stream()
    {
        var aggregator = new DownloadProgressAggregator(DownloadMode.Mp4Video);

        var videoFinished = aggregator.Aggregate(CreateProgress("id", "137", 100, "avc1", "none"));
        var audioStarted = aggregator.Aggregate(CreateProgress("id", "140", 10, "none", "mp4a"));

        Assert.AreEqual(50, videoFinished.Percent);
        Assert.AreEqual(55, audioStarted.Percent!.Value, 0.001);
    }

    [TestMethod]
    public void DownloadPlanParser_UsesRequestedFormatsAndSizes()
    {
        var parsed = YtDlpDownloadPlanParser.TryParse(
            "download-plan:video-id|137+140|1000|137|900|140|100|3|12",
            out var plan);

        Assert.IsTrue(parsed);
        Assert.AreEqual("video-id", plan.MediaId);
        Assert.HasCount(2, plan.Formats);
        Assert.AreEqual("137", plan.Formats[0].FormatId);
        Assert.AreEqual(900, plan.Formats[0].FileSize);
        Assert.AreEqual("140", plan.Formats[1].FormatId);
        Assert.AreEqual(100, plan.Formats[1].FileSize);
        Assert.AreEqual(3, plan.PlaylistIndex);
        Assert.AreEqual(12, plan.PlaylistCount);
    }

    [TestMethod]
    public void DownloadPlanParser_UsesCombinedFormatWhenNoRequestedFormatsExist()
    {
        var parsed = YtDlpDownloadPlanParser.TryParse(
            "download-plan:video-id|18|2500|NA|NA|NA|NA|NA|NA",
            out var plan);

        Assert.IsTrue(parsed);
        Assert.HasCount(1, plan.Formats);
        Assert.AreEqual("18", plan.Formats[0].FormatId);
        Assert.AreEqual(2500, plan.Formats[0].FileSize);
    }

    private static DownloadProgress CreateProgress(
        string mediaId,
        string formatId,
        double percent,
        string videoCodec,
        string audioCodec) =>
        new(
            percent,
            "1MiB/s",
            "1MiB/2MiB",
            "00:01",
            MediaId: mediaId,
            FormatId: formatId,
            VideoCodec: videoCodec,
            AudioCodec: audioCodec);
}
