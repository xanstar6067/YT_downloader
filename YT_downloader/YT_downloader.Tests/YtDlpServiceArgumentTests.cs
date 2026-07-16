using YT_downloader.Models;
using YT_downloader.Services;

namespace YT_downloader.Tests;

[TestClass]
public sealed class YtDlpServiceArgumentTests
{
    [TestMethod]
    public void AnalyzeAndDownload_UseMultiAudioClientAndJavaScriptRuntime()
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
            Assert.IsTrue(downloadArguments.Any(argument => argument.Contains("140-8", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(toolsDirectory, recursive: true);
        }
    }

    private static void AssertExtractionArguments(IReadOnlyList<string> arguments, string nodePath)
    {
        var runtimeIndex = arguments.IndexOf("--js-runtimes");
        var extractorIndex = arguments.IndexOf("--extractor-args");

        Assert.IsGreaterThanOrEqualTo(0, runtimeIndex);
        Assert.AreEqual($"node:{nodePath}", arguments[runtimeIndex + 1]);
        Assert.IsGreaterThanOrEqualTo(0, extractorIndex);
        Assert.AreEqual(
            "youtube:player_client=tv_downgraded,android_vr",
            arguments[extractorIndex + 1]);
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
