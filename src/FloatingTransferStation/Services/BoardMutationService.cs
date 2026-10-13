using FloatingTransferStation.Models;

namespace FloatingTransferStation.Services;

public enum BoardBatchMoveResult
{
    Invalid,
    NoChange,
    Moved,
    Failed
}

public enum BoardPinResult
{
    Invalid,
    NoChange,
    Changed,
    Failed
}

public sealed class BoardMutationService
{
    private readonly BoardService _board;
    private readonly IBoardStore _store;
    private readonly Action<string> _showStatus;
    private readonly BoardOperationGate _operationGate;

    public BoardMutationService(
        BoardService board,
        IBoardStore store,
        Action<string> showStatus,
        BoardOperationGate? operationGate = null)
    {
        _board = board;
        _store = store;
        _showStatus = showStatus;
        _operationGate = operationGate ?? new BoardOperationGate();
    }

    public async Task<bool> MoveAsync(
        Guid itemId,
        BoardCategory targetCategory,
        int targetIndex,
        CancellationToken cancellationToken = default)
    {
        return await _operationGate.RunAsync(async () =>
        {
            var move = _board.Move(itemId, targetCategory, targetIndex);
            try
            {
                await _store.SaveBoardAsync(_board.CreateSnapshot(), cancellationToken);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _board.Undo(move);
                _showStatus("移动未保存，内容已恢复到原位置。");
                return false;
            }
            catch
            {
                _board.Undo(move);
                throw;
            }
        }, cancellationToken);
    }

    public Task<BoardBatchMoveResult> MoveManyAsync(
        IReadOnlyCollection<Guid> itemIds,
        BoardCategory targetCategory,
        int targetIndex,
        CancellationToken cancellationToken = default) =>
        PersistMoveManyAsync(
            () => _board.MoveMany(itemIds, targetCategory, targetIndex),
            cancellationToken);

    public Task<BoardBatchMoveResult> MoveManyToCategoryTopAsync(
        IReadOnlyCollection<Guid> itemIds,
        BoardCategory targetCategory,
        CancellationToken cancellationToken = default) =>
        PersistMoveManyAsync(
            () => _board.MoveManyToCategoryTop(itemIds, targetCategory),
            cancellationToken);

    private Task<BoardBatchMoveResult> PersistMoveManyAsync(
        Func<BoardBatchMove> createMove,
        CancellationToken cancellationToken) =>
        _operationGate.RunAsync(async () =>
        {
            var move = createMove();
            if (!move.IsValid)
            {
                return BoardBatchMoveResult.Invalid;
            }

            if (!move.Changed)
            {
                return BoardBatchMoveResult.NoChange;
            }

            try
            {
                await _store.SaveBoardAsync(_board.CreateSnapshot(), cancellationToken);
                return BoardBatchMoveResult.Moved;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _board.Undo(move);
                _showStatus("移动未保存，内容已恢复到原位置。");
                return BoardBatchMoveResult.Failed;
            }
            catch
            {
                _board.Undo(move);
                throw;
            }
        }, cancellationToken);

    public Task<BoardPinResult> SetPinnedAsync(
        IReadOnlyCollection<Guid> itemIds,
        bool isPinned,
        CancellationToken cancellationToken = default) =>
        _operationGate.RunAsync(async () =>
        {
            BoardPinChange change;
            try
            {
                change = _board.SetPinnedMany(itemIds, isPinned);
            }
            catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
            {
                return BoardPinResult.Invalid;
            }

            if (!change.Changed)
            {
                return BoardPinResult.NoChange;
            }

            try
            {
                await _store.SaveBoardAsync(_board.CreateSnapshot(), cancellationToken);
                return BoardPinResult.Changed;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _board.Undo(change);
                _showStatus("置顶状态未保存，内容已恢复。");
                return BoardPinResult.Failed;
            }
            catch
            {
                _board.Undo(change);
                throw;
            }
        }, cancellationToken);

    public Task<bool> DeleteAsync(
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        DeleteManyAsync([itemId], cancellationToken);

    public Task<bool> DeleteManyAsync(
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default) =>
        _operationGate.RunAsync(async () =>
        {
            var removed = _board.RemoveMany(itemIds);
            if (removed is null)
            {
                return false;
            }

            try
            {
                await _store.SaveBoardAsync(_board.CreateSnapshot(), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _board.Restore(removed);
                _showStatus("删除未保存，内容已恢复。");
                return false;
            }
            catch
            {
                _board.Restore(removed);
                throw;
            }

            var imageCleanupFailed = false;
            foreach (var item in removed.RemovedItems.Where(item => item.Kind == BoardItemKind.File))
            {
                imageCleanupFailed |= !new FileAssetStore(_store.DataDirectory).TryDelete(item.FileAbsolutePath);
            }
            foreach (var item in removed.RemovedItems.Where(
                         item => item.Kind == BoardItemKind.Image))
            {
                imageCleanupFailed |= !_store.TryDeleteImage(item.ImageAbsolutePath);
            }

            if (imageCleanupFailed)
            {
                _showStatus("内容已删除，但部分图片副本暂时无法删除。");
            }

            return true;
        }, cancellationToken);

    public async Task<bool> ClearCategoryAsync(
        BoardCategory category,
        CancellationToken cancellationToken = default)
    {
        return await _operationGate.RunAsync(async () =>
        {
            var removed = _board.RemoveMany(_board.Items(category).Where(item => !item.IsPinned).Select(item => item.Id).ToArray());
            if (removed is null)
            {
                return false;
            }

            try
            {
                await _store.SaveBoardAsync(_board.CreateSnapshot(), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _board.Restore(removed);
                _showStatus("清空未保存，内容已恢复。");
                return false;
            }
            catch
            {
                _board.Restore(removed);
                throw;
            }

            var imageCleanupFailed = false;
            foreach (var item in removed.RemovedItems.Where(item => item.Kind == BoardItemKind.File))
            {
                imageCleanupFailed |= !new FileAssetStore(_store.DataDirectory).TryDelete(item.FileAbsolutePath);
            }
            foreach (var item in removed.RemovedItems.Where(item => item.Kind == BoardItemKind.Image))
            {
                imageCleanupFailed |= !_store.TryDeleteImage(item.ImageAbsolutePath);
            }

            if (imageCleanupFailed)
            {
                _showStatus("分类已清空，但部分图片副本暂时无法删除。");
            }

            return true;
        }, cancellationToken);
    }

    public Task<bool> RenameAsync(Guid id, string? name, CancellationToken cancellationToken = default) =>
        _operationGate.RunAsync(async () =>
        {
            var item = _board.Categories.SelectMany(_board.Items).FirstOrDefault(item => item.Id == id);
            if (item is null) { return false; }
            var previous = item.Name;
            item.Name = name;
            try { await _store.SaveBoardAsync(_board.CreateSnapshot(), cancellationToken); return true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                item.Name = previous;
                _showStatus("名称未保存，已恢复原名称。");
                return false;
            }
        }, cancellationToken);

    public Task<bool> ImportFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
        _operationGate.RunAsync(async () =>
        {
            var files = new FileAssetStore(_store.DataDirectory);
            var imported = new List<BoardItem>();
            try
            {
                foreach (var path in paths) { imported.Add(await files.ImportAsync(path, cancellationToken)); }
                foreach (var item in imported.AsEnumerable().Reverse()) { _board.AddFile(item); }
                await _store.SaveBoardAsync(_board.CreateSnapshot(), cancellationToken);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                foreach (var item in imported) { _board.Remove(item.Id); files.DiscardUnpublished(item); }
                _showStatus("文件未保存，本次已回滚，请重试。");
                return false;
            }
        }, cancellationToken);

    public Task SaveForShutdownAsync(
        Func<Task> saveAdditionalState,
        CancellationToken cancellationToken = default) =>
        _operationGate.SealAndRunAsync(async () =>
        {
            await _store.SaveBoardAsync(_board.CreateSnapshot(), cancellationToken);
            await saveAdditionalState();
        }, cancellationToken);

    public Task<bool> DeleteCategoryAsync(BoardCategory category, WindowSettings settings,
        CancellationToken cancellationToken = default) => _operationGate.RunAsync(async () =>
        {
            if (category is BoardCategory.Inbox or BoardCategory.Files) { return false; }
            try { await _store.SaveSettingsAsync(settings, cancellationToken); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _showStatus("分类删除未保存，原分类和内容保留。"); return false;
            }
            _board.DeletedCategoryIds = new HashSet<BoardCategory>(settings.DeletedCategoryIds);
            var ids = _board.Items(category).Select(item => item.Id).ToArray();
            if (ids.Length > 0) { _board.MoveManyToCategoryTop(ids, BoardCategory.Inbox); }
            try { await _store.SaveBoardAsync(_board.CreateSnapshot(), cancellationToken); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _showStatus("分类已删除，内容已移回待分类，记录稍后会自动保存。");
            }
            return true;
        }, cancellationToken);
}
