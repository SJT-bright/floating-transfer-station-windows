using FloatingTransferStation.Models;

namespace FloatingTransferStation.Services;

public interface IBoardStore
{
    string ImagesDirectory { get; }
    string DataDirectory => Path.GetDirectoryName(ImagesDirectory) ?? ImagesDirectory;
    string FilesDirectory => Path.Combine(DataDirectory, "files");
    Task<BoardSnapshot> LoadBoardAsync(CancellationToken cancellationToken = default);
    Task SaveBoardAsync(BoardSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<WindowSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(WindowSettings settings, CancellationToken cancellationToken = default);
    bool TryDeleteImage(string? absolutePath);
}
