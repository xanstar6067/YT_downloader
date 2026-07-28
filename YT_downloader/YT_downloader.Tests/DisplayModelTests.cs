using YT_downloader.Models;

namespace YT_downloader.Tests;

[TestClass]
public sealed class DisplayModelTests
{
    [TestMethod]
    public void ChoiceItem_ToString_ReturnsDisplayName()
    {
        var item = new ChoiceItem<string>("Лучшее доступное", "best");

        Assert.AreEqual("Лучшее доступное", item.ToString());
    }

    [TestMethod]
    public void AudioTrackInfo_ToString_ReturnsDisplayName()
    {
        var track = new AudioTrackInfo("251", "140", "Русский (ru)", "ru");

        Assert.AreEqual("Русский (ru)", track.ToString());
    }
}
