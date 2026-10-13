using FloatingTransferStation.Models;

namespace FloatingTransferStation.Services;

internal static class CaptureCommit
{
    public static async Task SaveAsync(BoardService board, IBoardStore store, BoardCategory category,
        CancellationToken cancellationToken)
    {
        var evicted = category == BoardCategory.Inbox ? board.EnforceInboxLimit(board.InboxItemLimit) : null;
        try { await store.SaveBoardAsync(board.CreateSnapshot(), cancellationToken); }
        catch
        {
            if (evicted is not null) { board.Restore(evicted); }
            throw;
        }
        if (evicted is not null)
        {
            foreach (var item in evicted.RemovedItems.Where(item => item.Kind == BoardItemKind.Image))
            {
                try { store.TryDeleteImage(item.ImageAbsolutePath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
        // Feedback and cleanup must not turn an already committed capture into a rollback.
        try { board.CaptureSucceeded?.Invoke(); }
        catch (Exception error) when (error is InvalidOperationException or IOException) { }
    }
}
