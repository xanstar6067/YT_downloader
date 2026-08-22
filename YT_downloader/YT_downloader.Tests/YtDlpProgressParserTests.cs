using YT_downloader.Services;

namespace YT_downloader.Tests;

[TestClass]
public sealed class YtDlpProgressParserTests
{
    [TestMethod]
    public void TryParse_CustomTemplate_ParsesAllFields()
    {
        var parsed = YtDlpProgressParser.TryParse(
            "download: 42.5%|3.21MiB/s|51.00MiB/120.00MiB|00:18",
            out var progress);

        Assert.IsTrue(parsed);
        Assert.AreEqual(42.5, progress.Percent);
        Assert.AreEqual("3.21MiB/s", progress.Speed);
        Assert.AreEqual("51.00MiB/120.00MiB", progress.FileSize);
        Assert.AreEqual("00:18", progress.RemainingTime);
    }

    [TestMethod]
    public void TryParse_CustomTemplate_AcceptsCommaDecimalSeparator()
    {
        var parsed = YtDlpProgressParser.TryParse(
            "download: 7,3%|950.00KiB/s|7.00MiB/95.00MiB|01:22",
            out var progress);

        Assert.IsTrue(parsed);
        Assert.AreEqual(7.3, progress.Percent);
    }

    [TestMethod]
    public void TryParse_PlaylistTemplate_ParsesItemPosition()
    {
        var parsed = YtDlpProgressParser.TryParse(
            "download: 28.5%|2.00MiB/s|10.00MiB/35.00MiB|00:12|3|18",
            out var progress);

        Assert.IsTrue(parsed);
        Assert.AreEqual(3, progress.PlaylistIndex);
        Assert.AreEqual(18, progress.PlaylistCount);
    }

    [TestMethod]
    public void TryParse_DetailedTemplate_ParsesStreamIdentityAndBytes()
    {
        var parsed = YtDlpProgressParser.TryParse(
            "download:video-id|137|5242880|10485760| 50.0%|2.00MiB/s|5.00MiB/10.00MiB|00:03|2|7|avc1.640028|none",
            out var progress);

        Assert.IsTrue(parsed);
        Assert.AreEqual(50.0, progress.Percent);
        Assert.AreEqual("video-id", progress.MediaId);
        Assert.AreEqual("137", progress.FormatId);
        Assert.AreEqual(5_242_880, progress.DownloadedBytes);
        Assert.AreEqual(10_485_760, progress.TotalBytes);
        Assert.AreEqual(2, progress.PlaylistIndex);
        Assert.AreEqual(7, progress.PlaylistCount);
        Assert.AreEqual("avc1.640028", progress.VideoCodec);
        Assert.AreEqual("none", progress.AudioCodec);
    }

    [TestMethod]
    public void TryParse_StandardYtDlpLine_ParsesProgress()
    {
        var parsed = YtDlpProgressParser.TryParse(
            "[download]  73.1% of 84.20MiB at 2.01MiB/s ETA 00:11",
            out var progress);

        Assert.IsTrue(parsed);
        Assert.AreEqual(73.1, progress.Percent);
        Assert.AreEqual("84.20MiB", progress.FileSize);
        Assert.AreEqual("2.01MiB/s", progress.Speed);
        Assert.AreEqual("00:11", progress.RemainingTime);
    }

    [TestMethod]
    public void TryParse_UnknownTemplateValues_ReplacesThemWithDash()
    {
        var parsed = YtDlpProgressParser.TryParse(
            "download: 0.0%|NA|0.00B/NA|N/A",
            out var progress);

        Assert.IsTrue(parsed);
        Assert.AreEqual("—", progress.Speed);
        Assert.AreEqual("0.00B/NA", progress.FileSize);
        Assert.AreEqual("—", progress.RemainingTime);
    }

    [TestMethod]
    public void TryParse_UnknownPercent_LeavesPercentEmpty()
    {
        var parsed = YtDlpProgressParser.TryParse(
            "download: NA%|NA|0.00B/NA|NA",
            out var progress);

        Assert.IsTrue(parsed);
        Assert.IsNull(progress.Percent);
    }

    [TestMethod]
    public void TryParse_UnrelatedLine_ReturnsFalse()
    {
        var parsed = YtDlpProgressParser.TryParse(
            "[Merger] Merging formats into output.mp4",
            out _);

        Assert.IsFalse(parsed);
    }
}
