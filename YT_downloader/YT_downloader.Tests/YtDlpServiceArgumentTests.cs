using YT_downloader.Models;
using YT_downloader.Services;

namespace YT_downloader.Tests;

[TestClass]
public sealed class YtDlpServiceArgumentTests
{
    [TestMethod]
    public void AnalyzeAndDownload_UseJavaScriptRuntimeAndLetYtDlpChooseYouTubeClients()
    {
        var toolsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(toolsDirectory);

        try
        {
            var nodePath = Path.Combine(toolsDirectory, "node.exe");
            File.WriteAllBytes(nodePath, []);
            var service = new YtDlpService(toolsDirectory);

            var analyzeArguments = service.BuildAnalyzeArguments("https://www.youtube.com/watch?v=test");
            var downloadArguments = service.BuildDownloadArguments(new DownloadRequest(
                "https://www.youtube.com/watch?v=test",
                toolsDirectory,
                DownloadMode.Mp4Video,
                "1080",
                "140-8"));

            AssertExtractionArguments(analyzeArguments, nodePath);
            AssertExtractionArguments(downloadArguments, nodePath);
            CollectionAssert.DoesNotContain(analyzeArguments.ToArray(), "--extractor-args");
            CollectionAssert.DoesNotContain(downloadArguments.ToArray(), "--extractor-args");
            CollectionAssert.Contains(analyzeArguments.ToArray(), "--encoding");
            CollectionAssert.Contains(downloadArguments.ToArray(), "--encoding");
            CollectionAssert.Contains(analyzeArguments.ToArray(), "--no-playlist");
            CollectionAssert.Contains(downloadArguments.ToArray(), "--no-playlist");
            CollectionAssert.Contains(downloadArguments.ToArray(), "--print");
            CollectionAssert.Contains(downloadArguments.ToArray(), "--no-quiet");
            Assert.IsTrue(downloadArguments.Any(argument => argument.Contains("140-8", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(toolsDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void PlaylistArguments_EnablePlaylistAndUseNumberedSubfolder()
    {
        var toolsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(toolsDirectory);

        try
        {
            var service = new YtDlpService(toolsDirectory);
            var analyzeArguments = service.BuildAnalyzeArguments(
                "https://www.youtube.com/playlist?list=test",
                includePlaylist: true);
            var downloadArguments = service.BuildDownloadArguments(new DownloadRequest(
                "https://www.youtube.com/playlist?list=test",
                toolsDirectory,
                DownloadMode.Mp3Audio,
                "best",
                null,
                DownloadPlaylist: true));

            CollectionAssert.Contains(analyzeArguments.ToArray(), "--yes-playlist");
            CollectionAssert.Contains(analyzeArguments.ToArray(), "--flat-playlist");
            CollectionAssert.DoesNotContain(analyzeArguments.ToArray(), "--no-playlist");
            CollectionAssert.Contains(downloadArguments.ToArray(), "--yes-playlist");
            CollectionAssert.DoesNotContain(downloadArguments.ToArray(), "--no-playlist");

            var outputIndex = downloadArguments.IndexOf("--output");
            Assert.IsGreaterThanOrEqualTo(0, outputIndex);
            StringAssert.Contains(downloadArguments[outputIndex + 1], "%(playlist_title).120B");
            StringAssert.Contains(downloadArguments[outputIndex + 1], "%(playlist_index)03d");
        }
        finally
        {
            Directory.Delete(toolsDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ForbiddenDownloadDetection_RecognizesYtDlpHttp403()
    {
        Assert.IsTrue(YtDlpService.IsForbiddenDownloadError(
            "ERROR: unable to download video data: HTTP Error 403: Forbidden"));
        Assert.IsFalse(YtDlpService.IsForbiddenDownloadError(
            "ERROR: unable to download video data: HTTP Error 404: Not Found"));
    }

    [TestMethod]
    public void BrowserCookies_AreOnlyAddedWhenExplicitlySelected()
    {
        var service = new YtDlpService(Path.GetTempPath());

        var anonymousArguments = service.BuildAnalyzeArguments("https://example.test/video");
        var browserArguments = service.BuildAnalyzeArguments(
            "https://example.test/video",
            browserCookieSource: BrowserCookieSource.Edge);
        var downloadArguments = service.BuildDownloadArguments(new DownloadRequest(
            "https://example.test/video",
            Path.GetTempPath(),
            DownloadMode.Mp3Audio,
            "best",
            null,
            BrowserCookieSource: BrowserCookieSource.Firefox));

        CollectionAssert.DoesNotContain(anonymousArguments.ToArray(), "--cookies-from-browser");
        AssertOptionValue(browserArguments, "--cookies-from-browser", "edge");
        AssertOptionValue(downloadArguments, "--cookies-from-browser", "firefox");
    }

    [TestMethod]
    public void BotVerificationDetection_IsSeparateFromUnavailableVideo()
    {
        Assert.IsTrue(YtDlpService.IsBotVerificationError(
            "ERROR: Sign in to confirm you're not a bot. Use --cookies-from-browser or --cookies for the authentication"));
        Assert.IsFalse(YtDlpService.IsBotVerificationError(
            "ERROR: This video is unavailable"));
    }

    private static void AssertExtractionArguments(IReadOnlyList<string> arguments, string nodePath)
    {
        var runtimeIndex = arguments.IndexOf("--js-runtimes");
        var extractorIndex = arguments.IndexOf("--extractor-args");

        Assert.IsGreaterThanOrEqualTo(0, runtimeIndex);
        Assert.AreEqual($"node:{nodePath}", arguments[runtimeIndex + 1]);
        Assert.AreEqual(-1, extractorIndex);
    }

    private static void AssertOptionValue(
        IReadOnlyList<string> arguments,
        string option,
        string expectedValue)
    {
        var optionIndex = arguments.IndexOf(option);
        Assert.IsGreaterThanOrEqualTo(0, optionIndex);
        Assert.AreEqual(expectedValue, arguments[optionIndex + 1]);
    }
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> source, string value)
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (string.Equals(source[index], value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
