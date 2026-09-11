using FloatingTransferStation.Models;
using FloatingTransferStation.Services;
using FloatingTransferStation.ViewModels;

namespace FloatingTransferStation.Tests;

[TestClass]
public sealed class WindowsPortTests
{
    [TestMethod]
    public async Task CustomCategories_RoundTripWithContentAndEmptyCategories()
    {
        using var directory = new TestDirectory();
        var store = new LocalStore(AppPaths.ForTests(directory.Root), new AtomicTextWriter());
        var custom = (BoardCategory)1000;
        var empty = (BoardCategory)1001;
        var settings = WindowSettings.Default.WithCategoryName(custom, "道具")
            .WithCategoryName(empty, "素材").WithCategoryName(BoardCategory.Prompt, "提示词");
        var board = new BoardService();
        var original = board.AddText("不会丢失的内容", custom);
        await store.SaveBoardAsync(board.CreateSnapshot());
        await store.SaveSettingsAsync(settings);

        var restored = new BoardService();
        restored.Restore(await store.LoadBoardAsync());
        var viewModel = new MainWindowViewModel(restored, await store.LoadSettingsAsync());
        Assert.AreEqual(6, viewModel.Categories.Count);
        Assert.AreEqual("道具", viewModel.Categories.Single(item => item.Category == custom).DisplayName);
        Assert.AreEqual(original.Id, restored.Items(custom).Single().Id);
        Assert.AreEqual(0, restored.Items(empty).Count);
        Assert.AreEqual(BoardCategory.Inbox, viewModel.DefaultCapturePanel.Category);
        Assert.AreEqual(0d, new CategoryScrollState().GetClamped(custom, 300));
    }
}
