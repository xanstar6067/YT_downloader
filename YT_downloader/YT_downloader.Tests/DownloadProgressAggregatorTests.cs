using YT_downloader.Models;
using YT_downloader.Services;

namespace YT_downloader.Tests;

[TestClass]
public sealed class DownloadProgressAggregatorTests
{
    [TestMethod]
    public void Aggregate_SeparateStreams_UsesTwoMonotonicStages()
    {
        var aggregator = new DownloadProgressAggregator(DownloadMode.Mp4Video);

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

        Assert.AreEqual(50, videoFinished.Percent);
        Assert.AreEqual(60, audioStarted.Percent);
        Assert.AreEqual("Загрузка видео", videoFinished.Status);
        Assert.AreEqual("Загрузка аудио", audioStarted.Status);
    }

    [TestMethod]
    public void Aggregate_PercentEstimateMovesBackward_KeepsPreviousValue()
    {
        var aggregator = new DownloadProgressAggregator(DownloadMode.Mp3Audio);

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
    public void Aggregate_OCgr08Q7A6cHlsBootstrap_DoesNotJumpToHalf()
    {
        var aggregator = new DownloadProgressAggregator(DownloadMode.Mp4Video);
        var bootstrap = aggregator.Aggregate(CreateProgress(
            "id",
            "616",
            100,
            "vp9",
            "none") with
        {
            FragmentIndex = 0,
            FragmentCount = 54
        });
        var firstFragment = aggregator.Aggregate(CreateProgress(
            "id",
            "616",
            0.9,
            "vp9",
            "none") with
        {
            FragmentIndex = 1,
            FragmentCount = 54
        });
        var middle = aggregator.Aggregate(CreateProgress(
            "id",
            "616",
            57.4,
            "vp9",
            "none") with
        {
            FragmentIndex = 30,
            FragmentCount = 54
        });

        Assert.AreEqual(0, bootstrap.Percent);
        Assert.AreEqual(100d / 54 / 2, firstFragment.Percent!.Value, 0.001);
        Assert.AreEqual(30d / 54 * 50, middle.Percent!.Value, 0.001);
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
