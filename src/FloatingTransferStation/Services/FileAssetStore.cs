using FloatingTransferStation.Models;

namespace FloatingTransferStation.Services;

public sealed class FileAssetStore
{
    private readonly string _dataDirectory;

    public FileAssetStore(string dataDirectory) => _dataDirectory = Path.GetFullPath(dataDirectory);

    public static string ResolvePath(string dataDirectory, string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/');
        if (parts.Length != 3 || parts[0] != "files" || !Guid.TryParse(parts[1], out _) ||
            string.IsNullOrWhiteSpace(parts[2]) || parts[2] is "." or ".." ||
            parts[2].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("文件路径不在中转站管理目录内。");
        }
        var current = Path.Combine(Path.GetFullPath(dataDirectory), "files");
        foreach (var part in parts.Skip(1))
        {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("不支持符号链接目录。");
            }
            current = Path.Combine(current, part);
        }
        if (File.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("不支持符号链接文件。");
        }
        return current;
    }

    public async Task<BoardItem> ImportAsync(string source, CancellationToken cancellationToken)
    {
        if (!File.Exists(source) || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("请选择普通文件，不支持文件夹或符号链接。");
        }
        var id = Guid.NewGuid();
        var name = Path.GetFileName(source);
        var relative = $"files/{id:D}/{name}";
        var destination = ResolvePath(_dataDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                              81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                               81920, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
            var item = new BoardItem
            {
                Id = id,
                Kind = BoardItemKind.File,
                Category = BoardCategory.Files,
                Order = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                FileRelativePath = relative,
                FileAbsolutePath = destination,
                FileName = name,
                FileSize = new FileInfo(destination).Length
            };
            await Task.Run(() => EnsureExport(item), cancellationToken).ConfigureAwait(false);
            return item;
        }
        catch
        {
            TryDelete(destination);
            throw;
        }
    }

    public string EnsureExport(BoardItem item)
    {
        var source = ResolvePath(_dataDirectory, item.FileRelativePath ?? throw new InvalidDataException("文件路径缺失。"));
        if (!File.Exists(source)) { throw new FileNotFoundException("文件副本已缺失。", source); }
        var destination = Path.Combine(_dataDirectory, "drag-exports", "files", item.Id.ToString("D"), Path.GetFileName(source));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!File.Exists(destination)) { File.Copy(source, destination, overwrite: false); }
        return destination;
    }

    public bool TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) { return true; }
        var full = Path.GetFullPath(path);
        var prefix = Path.Combine(_dataDirectory, "files") + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return false; }
        try
        {
            if (File.Exists(full)) { File.Delete(full); }
            var directory = Path.GetDirectoryName(full)!;
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) { Directory.Delete(directory); }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public void DiscardUnpublished(BoardItem item)
    {
        TryDelete(item.FileAbsolutePath);
        var directory = Path.Combine(_dataDirectory, "drag-exports", "files", item.Id.ToString("D"));
        if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
    }
}
