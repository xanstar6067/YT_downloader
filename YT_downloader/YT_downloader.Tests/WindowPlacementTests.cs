using System.Text.Json;
using System.Windows;
using YT_downloader.Models;
using YT_downloader.Services;

namespace YT_downloader.Tests;

[TestClass]
public sealed class WindowPlacementTests
{
    [TestMethod]
    public void FirstLaunch_Fits1600By900WorkArea()
    {
        var placement = WindowPlacementService.FitToWorkArea(null, new Rect(0, 0, 1600, 860), 1);

        Assert.AreEqual(new WindowPlacement(250, 60, 1100, 740, false), placement);
    }

    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(2.0)]
    public void OversizedWindow_IsClampedToWorkAreaAtAnyScale(double scale)
    {
        var workArea = new Rect(0, 40, 1600, 860);
        var placement = WindowPlacementService.FitToWorkArea(new WindowPlacement(-100, -200, 2200, 1200, false), workArea, scale);

        Assert.AreEqual(new WindowPlacement(0, 40, 1600, 860, false), placement);
    }

    [TestMethod]
    public void DisconnectedMonitor_WindowMovesEntirelyOntoRemainingScreen()
    {
        var placement = WindowPlacementService.FitToWorkArea(new WindowPlacement(3000, 1000, 1100, 780, true), new Rect(0, 0, 1600, 860), 1);

        Assert.AreEqual(new WindowPlacement(500, 80, 1100, 780, true), placement);
    }

    [TestMethod]
    public void MonitorToLeftOfPrimary_PreservesNegativeCoordinates()
    {
        var saved = new WindowPlacement(-1450, 50, 1100, 780, false);

        Assert.AreEqual(saved, WindowPlacementService.FitToWorkArea(saved, new Rect(-1600, 0, 1600, 860), 1));
    }

    [TestMethod]
    public void SmallWorkArea_TakesPriorityOverMinimumWindowSize()
    {
        var placement = WindowPlacementService.FitToWorkArea(null, new Rect(0, 0, 640, 400), 1.5);

        Assert.AreEqual(new WindowPlacement(0, 0, 640, 400, false), placement);
    }

    [TestMethod]
    public void ValidSavedPositionAndSize_AreUnchanged()
    {
        var saved = new WindowPlacement(120, 65, 1200, 750, false);

        Assert.AreEqual(saved, WindowPlacementService.FitToWorkArea(saved, new Rect(0, 0, 1600, 860), 1));
    }

    [TestMethod]
    public void Settings_RoundTripPlacementAndReadOldFiles()
    {
        var settings = new AppSettings { WindowPlacement = new WindowPlacement(-1400, 80, 1100, 780, true) };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings));

        Assert.AreEqual(settings.WindowPlacement, restored!.WindowPlacement);
        Assert.IsNull(JsonSerializer.Deserialize<AppSettings>("{\"SaveFolder\":\"D:\\\\Videos\"}")!.WindowPlacement);
    }
}
