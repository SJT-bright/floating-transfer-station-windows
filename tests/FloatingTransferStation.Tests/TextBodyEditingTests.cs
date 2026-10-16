using FloatingTransferStation.Models;
using FloatingTransferStation.Services;

namespace FloatingTransferStation.Tests;

[TestClass]
public sealed class TextBodyEditingTests
{
    [TestMethod]
    public async Task EditPersistsBodyWithoutChangingIdentityOrMetadata()
    {
        using var directory = new TestDirectory();
        var paths = AppPaths.ForTests(directory.Root);
        var store = new LocalStore(paths, new AtomicTextWriter());
        await store.LoadBoardAsync();
        var board = new BoardService();
        var item = board.AddText("旧正文", BoardCategory.Prompt);
        item.Name = "固定名称"; item.IsPinned = true;
        var original = item.CloneForSnapshot();
        var mutation = new BoardMutationService(board, store, _ => { });
        var updated = "  第一行 👩🏽‍💻\r\n第二行\n末行  ";
        Assert.AreEqual(BoardTextEditResult.Saved, await mutation.EditTextAsync(item.Id, updated, "旧正文"));
        var reloaded = (await store.LoadBoardAsync()).Items.Single();
        Assert.AreEqual(updated, item.Text); Assert.AreEqual(updated, reloaded.Text);
        Assert.AreEqual(original.Id, reloaded.Id); Assert.AreEqual(original.Name, reloaded.Name);
        Assert.AreEqual(original.Category, reloaded.Category); Assert.AreEqual(original.IsPinned, reloaded.IsPinned);
        Assert.AreEqual(original.CreatedAt, reloaded.CreatedAt); Assert.AreEqual(original.ArrivalSequence, reloaded.ArrivalSequence);
        Assert.AreEqual(original.Order, reloaded.Order);
        Assert.AreEqual(BoardTextEditResult.Unchanged, await mutation.EditTextAsync(item.Id, updated, updated));
    }

    [TestMethod]
    public async Task InvalidConflictAndDeletedRecordsDoNotOverwriteOriginal()
    {
        using var directory = new TestDirectory();
        var store = new LocalStore(AppPaths.ForTests(directory.Root), new AtomicTextWriter());
        await store.LoadBoardAsync();
        var board = new BoardService(); var item = board.AddText("原文");
        var mutation = new BoardMutationService(board, store, _ => { });
        Assert.AreEqual(BoardTextEditResult.Invalid, await mutation.EditTextAsync(item.Id, " \n\t", "原文"));
        Assert.AreEqual(BoardTextEditResult.Conflict, await mutation.EditTextAsync(item.Id, "新文", "过期原文"));
        Assert.AreEqual("原文", item.Text);
        board.Remove(item.Id);
        Assert.AreEqual(BoardTextEditResult.Invalid, await mutation.EditTextAsync(item.Id, "新文", "原文"));
        Assert.IsEmpty(board.CreateSnapshot().Items);
    }

    [TestMethod]
    public async Task FailedWriteNeverPublishesBodyAndCanRetry()
    {
        using var directory = new TestDirectory();
        var board = new BoardService(); var item = board.AddText("原文");
        var store = new FailingFirstSaveBoardStore(directory.Root);
        var mutation = new BoardMutationService(board, store, _ => { });
        var task = mutation.EditTextAsync(item.Id, "新文", "原文");
        await store.FirstSaveStarted.Task;
        Assert.AreEqual("原文", item.Text);
        store.FailFirstSave();
        Assert.AreEqual(BoardTextEditResult.Failed, await task);
        Assert.AreEqual("原文", item.Text);
        Assert.AreEqual(BoardTextEditResult.Saved, await mutation.EditTextAsync(item.Id, "新文", "原文"));
        Assert.AreEqual("新文", item.Text);
    }
}
