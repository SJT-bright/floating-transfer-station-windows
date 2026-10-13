using FloatingTransferStation.Models;
using FloatingTransferStation.Services;

namespace FloatingTransferStation.Tests;

[TestClass]
public sealed class StationParityTests
{
    [TestMethod]
    public async Task NamesAndMissingAssetMetadataSurviveSaveAndReload()
    {
        using var directory = new TestDirectory();
        var paths = AppPaths.ForTests(directory.Root);
        var store = new LocalStore(paths, new AtomicTextWriter());
        var item = BoardItem.CreateImage(Guid.NewGuid(), "images/missing.png", Path.Combine(paths.ImagesDirectory, "missing.png"), DateTimeOffset.UtcNow);
        item.Name = "保留的名称"; item.IsPinned = true;
        await store.SaveBoardAsync(new BoardSnapshot { Items = [item] });
        var loaded = await store.LoadBoardAsync();
        Assert.AreEqual(1, loaded.Items.Count); Assert.AreEqual(item.Name, loaded.Items[0].Name); Assert.IsTrue(loaded.Items[0].IsPinned);
        await store.SaveBoardAsync(loaded);
        Assert.AreEqual(item.Id, (await store.LoadBoardAsync()).Items.Single().Id);
    }

    [TestMethod]
    public async Task FutureSchemaCannotBeOverwritten()
    {
        using var directory = new TestDirectory();
        var paths = AppPaths.ForTests(directory.Root);
        await File.WriteAllTextAsync(paths.BoardFile, "{\"schemaVersion\":999,\"items\":[]}");
        var before = await File.ReadAllBytesAsync(paths.BoardFile);
        var store = new LocalStore(paths, new AtomicTextWriter());
        await store.LoadBoardAsync();
        await Assert.ThrowsExactlyAsync<IOException>(() => store.SaveBoardAsync(new BoardSnapshot()));
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(paths.BoardFile));
    }

    [TestMethod]
    public async Task ClearProtectsPinsAndRenameRollsBackFailure()
    {
        using var directory = new TestDirectory();
        var board = new BoardService();
        var pinned = board.AddText("pinned"); pinned.IsPinned = true;
        board.AddText("normal");
        var store = new MemoryStore(directory.Root);
        var mutation = new BoardMutationService(board, store, _ => { });
        Assert.IsTrue(await mutation.ClearCategoryAsync(BoardCategory.Inbox));
        CollectionAssert.AreEqual(new[] { pinned.Id }, board.Items(BoardCategory.Inbox).Select(item => item.Id).ToArray());
        Assert.IsTrue(await mutation.RenameAsync(pinned.Id, "新名字"));
        Assert.AreEqual("新名字", pinned.Name);
        store.FailBoard = true;
        Assert.IsFalse(await mutation.RenameAsync(pinned.Id, "失败的名字"));
        Assert.AreEqual("新名字", pinned.Name);
        Assert.IsFalse(await mutation.ClearCategoryAsync(BoardCategory.Inbox));
    }

    [TestMethod]
    public async Task CapacityUsesArrivalNotReorderAndFailedSaveRestoresEvictedRecords()
    {
        using var directory = new TestDirectory();
        var board = new BoardService { InboxItemLimit = 1 };
        var older = board.AddText("older");
        var pin = board.AddText("pin"); pin.IsPinned = true;
        board.Move(older.Id, BoardCategory.Inbox, 0);
        var store = new MemoryStore(directory.Root);
        var capture = new ClipboardCaptureService(new Reader("new"), new ImageNormalizer(store.ImagesDirectory), board, store, _ => { });
        await capture.HandleClipboardUpdateAsync();
        Assert.IsFalse(board.Items(BoardCategory.Inbox).Any(item => item.Id == older.Id));
        Assert.IsTrue(board.Items(BoardCategory.Inbox).Any(item => item.Id == pin.Id));
        var before = board.Items(BoardCategory.Inbox).Select(item => item.Id).ToArray();
        store.FailBoard = true;
        await new ClipboardCaptureService(new Reader("failed"), new ImageNormalizer(store.ImagesDirectory), board, store, _ => { }).HandleClipboardUpdateAsync();
        CollectionAssert.AreEqual(before, board.Items(BoardCategory.Inbox).Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public async Task FileBatchPreservesNamesBytesAndCopiesPersistOnce()
    {
        using var directory = new TestDirectory();
        var data = Path.Combine(directory.Root, "data");
        var paths = AppPaths.ForTests(data);
        var store = new LocalStore(paths, new AtomicTextWriter());
        var board = new BoardService();
        var first = Path.Combine(directory.Root, "first.txt"); var second = Path.Combine(directory.Root, "second.zip");
        await File.WriteAllBytesAsync(first, [1, 2, 255]); await File.WriteAllBytesAsync(second, [8, 9, 0]);
        var mutation = new BoardMutationService(board, store, _ => { });
        Assert.IsTrue(await mutation.ImportFilesAsync([first, second]));
        var files = board.Items(BoardCategory.Files).ToArray();
        CollectionAssert.AreEqual(new[] { "first.txt", "second.zip" }, files.Select(file => file.FileName).ToArray());
        CollectionAssert.AreEqual(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(files[0].FileAbsolutePath!));
        CollectionAssert.AreEqual(await File.ReadAllBytesAsync(second), await File.ReadAllBytesAsync(files[1].FileAbsolutePath!));
        Assert.AreEqual(2, (await store.LoadBoardAsync()).Items.Count);
        var export = new FileAssetStore(data).EnsureExport(files[0]);
        Assert.IsTrue(await mutation.DeleteAsync(files[0].Id));
        Assert.IsTrue(File.Exists(export)); Assert.IsTrue(File.Exists(first));
    }

    [TestMethod]
    public async Task FailedFileBatchHasNoPartialRecordsAndDoesNotTouchOriginal()
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Root, "original.dat"); await File.WriteAllBytesAsync(source, [10, 20]);
        var store = new MemoryStore(Path.Combine(directory.Root, "data")) { FailBoard = true };
        var board = new BoardService(); var service = new BoardMutationService(board, store, _ => { });
        Assert.IsFalse(await service.ImportFilesAsync([source]));
        Assert.IsEmpty(board.Items(BoardCategory.Files)); Assert.IsTrue(File.Exists(source));
        Assert.IsFalse(Directory.EnumerateFiles(store.FilesDirectory, "*", SearchOption.AllDirectories).Any());
    }

    [TestMethod]
    public async Task DeleteCategoryKeepsAllContentAndRollsBackFailedSettings()
    {
        using var directory = new TestDirectory();
        var board = new BoardService(); var item = board.AddText("keep me", BoardCategory.Reference); item.Name = "名称"; item.IsPinned = true;
        var store = new MemoryStore(directory.Root) { FailSettings = true }; var service = new BoardMutationService(board, store, _ => { });
        var settings = WindowSettings.Default with { DeletedCategoryIds = [BoardCategory.Reference] };
        Assert.IsFalse(await service.DeleteCategoryAsync(BoardCategory.Reference, settings)); Assert.AreEqual(BoardCategory.Reference, item.Category);
        store.FailSettings = false; Assert.IsTrue(await service.DeleteCategoryAsync(BoardCategory.Reference, settings));
        Assert.AreEqual(BoardCategory.Inbox, item.Category); Assert.AreEqual("名称", item.Name); Assert.IsTrue(item.IsPinned);
    }

    [TestMethod]
    public async Task OriginalPngBytesSurviveImportAndNamedCategoryBatch()
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Root, "source.png");
        using (var image = new Imaging.Image<Imaging.Rgba32>(4, 2, new Imaging.Rgba32(200, 80, 10, 255))) { image.Save(source); }
        var bytes = await File.ReadAllBytesAsync(source);
        var normalizer = new ImageNormalizer(Path.Combine(directory.Root, "images"));
        var stored = await normalizer.NormalizeFileAsync(source);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(stored.AbsolutePath));
        var board = new BoardService(); var store = new MemoryStore(directory.Root);
        var importer = new ExternalDropImportService(normalizer, board, store, _ => { });
        Assert.IsTrue(await importer.ImportAsync(new ExternalDropPayload.ImageFiles([source], ["原图名称"]), BoardCategory.Reference));
        var copy = board.Items(BoardCategory.Reference).Single();
        Assert.AreEqual("原图名称", copy.Name); CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(copy.ImageAbsolutePath!));
    }

    [TestMethod]
    public void FreeFloatingAndLeftDockPlacementPersistWithoutOffscreenGeometry()
    {
        var area = new WorkArea(0, 0, 1920, 1040);
        var settings = WindowSettings.Default with { FreeFloating = true, WindowX = 600, Top = 120 };
        Assert.AreEqual(600, WindowController.Expanded(area, settings).Left);
        Assert.AreEqual(0, WindowController.Expanded(area, settings with { FreeFloating = false, DockLeft = true }).Left);
        Assert.AreEqual(0, WindowController.Collapsed(area, settings with { FreeFloating = false, DockLeft = true }, BoardCategory.Inbox).Left);
    }

    [TestMethod]
    public void AppearanceResetKeepsDiyCapacityAndDeletionChoices()
    {
        var settings = WindowSettings.Default with { CopySuccessSoundEnabled = false, TextHoverExpansionEnabled = false, InboxItemLimit = 100, DeletedCategoryIds = [BoardCategory.Reference] };
        var reset = settings.ResetToDefault(1920, 1040);
        Assert.IsFalse(reset.CopySuccessSoundEnabled); Assert.IsFalse(reset.TextHoverExpansionEnabled); Assert.AreEqual(100, reset.InboxItemLimit);
        Assert.IsTrue(reset.DeletedCategoryIds.Contains(BoardCategory.Reference));
    }

    private sealed class Reader(string value) : IClipboardReader
    {
        public Task<ClipboardSnapshot> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ClipboardSnapshot(1, null, [], value));
    }
    private sealed class MemoryStore(string root) : IBoardStore
    {
        public string ImagesDirectory => Path.Combine(root, "images");
        public string DataDirectory => root;
        public string FilesDirectory => Path.Combine(root, "files");
        public bool FailBoard { get; set; }
        public bool FailSettings { get; set; }
        public Task<BoardSnapshot> LoadBoardAsync(CancellationToken cancellationToken = default) => Task.FromResult(new BoardSnapshot());
        public Task SaveBoardAsync(BoardSnapshot snapshot, CancellationToken cancellationToken = default) => FailBoard ? Task.FromException(new IOException("test failure")) : Task.CompletedTask;
        public Task<WindowSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(WindowSettings.Default);
        public Task SaveSettingsAsync(WindowSettings settings, CancellationToken cancellationToken = default) => FailSettings ? Task.FromException(new IOException("test failure")) : Task.CompletedTask;
        public bool TryDeleteImage(string? path) { if (path is not null && File.Exists(path)) { File.Delete(path); } return true; }
    }
}
