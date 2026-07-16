using System.Text.Json;
using YT_downloader.Services;

namespace YT_downloader.Tests;

[TestClass]
public sealed class YtDlpMetadataParserTests
{
    [TestMethod]
    public void Parse_GroupsCodecVariantsByLanguage()
    {
        const string json = """
            {
              "title": "Видео с озвучками",
              "thumbnail": "https://example.test/thumb.jpg",
              "duration_string": "12:34",
              "formats": [
                { "format_id": "140-0", "ext": "m4a", "vcodec": "none", "acodec": "mp4a.40.2", "language": "ru", "abr": 128, "language_preference": 10 },
                { "format_id": "251-0", "ext": "webm", "vcodec": "none", "acodec": "opus", "language": "ru", "abr": 160, "language_preference": 10 },
                { "format_id": "251-1", "ext": "webm", "vcodec": "none", "acodec": "opus", "language": "en", "abr": 150, "language_preference": -1 },
                { "format_id": "140-1", "ext": "m4a", "vcodec": "none", "acodec": "mp4a.40.2", "language": "en", "abr": 128, "language_preference": -1 },
                { "format_id": "140-drc", "ext": "m4a", "vcodec": "none", "acodec": "mp4a.40.2", "language": "en", "abr": 160, "language_preference": -1, "format_note": "English, medium, DRC" },
                { "format_id": "137", "ext": "mp4", "vcodec": "avc1", "acodec": "none", "language": null }
              ]
            }
            """;

        var video = YtDlpMetadataParser.Parse(json);

        Assert.AreEqual("Видео с озвучками", video.Title);
        Assert.HasCount(2, video.AudioTracks);

        var russian = video.AudioTracks.Single(track => track.LanguageCode == "ru");
        Assert.AreEqual("251-0", russian.BestFormatId);
        Assert.AreEqual("140-0", russian.Mp4FormatId);
        StringAssert.Contains(russian.DisplayName, "оригинал");

        var english = video.AudioTracks.Single(track => track.LanguageCode == "en");
        Assert.AreEqual("251-1", english.BestFormatId);
        Assert.AreEqual("140-1", english.Mp4FormatId);
    }

    [TestMethod]
    public void Parse_WithoutLanguageMetadata_ReturnsNoExplicitTracks()
    {
        const string json = """
            {
              "title": "Обычное видео",
              "formats": [
                { "format_id": "140", "ext": "m4a", "vcodec": "none", "acodec": "mp4a.40.2", "abr": 128 }
              ]
            }
            """;

        var video = YtDlpMetadataParser.Parse(json);

        Assert.IsEmpty(video.AudioTracks);
    }

    [TestMethod]
    public void Parse_MultipleDubLanguages_ReturnsEveryTrack()
    {
        string[] languages =
        [
            "ar", "de", "en", "es", "fil", "fr", "hu", "id", "it", "ja", "ko",
            "nl", "pl", "pt", "ru", "th", "tr", "uk", "ur", "vi", "zh-Hans"
        ];
        var formats = languages.Select((language, index) => new
        {
            format_id = $"140-{index}",
            ext = "m4a",
            vcodec = "none",
            acodec = "mp4a.40.2",
            language,
            abr = 128,
            language_preference = language == "en" ? 10 : -1
        });
        var json = JsonSerializer.Serialize(new { title = "Видео с 21 озвучкой", formats });

        var video = YtDlpMetadataParser.Parse(json);

        Assert.HasCount(21, video.AudioTracks);
        CollectionAssert.AreEquivalent(
            languages,
            video.AudioTracks.Select(track => track.LanguageCode).ToArray());
    }
}
