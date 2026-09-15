using FloatingTransferStation.Models;
using FloatingTransferStation.Services;

namespace FloatingTransferStation.Tests;

[TestClass]
public sealed class TransparencySettingsTests
{
    [TestMethod]
    public void Normalize_ClampsUnsafeValuesAndKeepsUsableDefault()
    {
        Assert.AreEqual(
            WindowSettings.DefaultWindowOpacity,
            new WindowSettings(360, 640, 80, WindowOpacity: 0).Normalize(1920, 1080).WindowOpacity,
            0.0001);
        Assert.AreEqual(
            WindowSettings.MinWindowOpacity,
            new WindowSettings(360, 640, 80, WindowOpacity: -1).Normalize(1920, 1080).WindowOpacity,
            0.0001);
        Assert.AreEqual(
            WindowSettings.MaxWindowOpacity,
            new WindowSettings(360, 640, 80, WindowOpacity: 2).Normalize(1920, 1080).WindowOpacity,
            0.0001);
    }

    [TestMethod]
    public async Task LocalStore_RoundTripsWindowOpacity()
    {
        using var directory = new TestDirectory();
        var store = new LocalStore(AppPaths.ForTests(directory.Root), new AtomicTextWriter());
        var settings = WindowSettings.Default with { WindowOpacity = 0.47 };

        await store.SaveSettingsAsync(settings);
        var loaded = await store.LoadSettingsAsync();

        Assert.AreEqual(0.47, loaded.WindowOpacity, 0.0001);
    }
}
