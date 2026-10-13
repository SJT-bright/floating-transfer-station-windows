using System.Windows.Media.Imaging;
using FloatingTransferStation.Models;

namespace FloatingTransferStation.Services;

public sealed class ImageNormalizer : IImageNormalizer
{
    private readonly string _imagesDirectory;

    public ImageNormalizer(string imagesDirectory)
    {
        _imagesDirectory = imagesDirectory;
    }

    public Task<StoredImage> NormalizeFileAsync(
        string sourcePath,
        Guid? id = null,
        CancellationToken cancellationToken = default) =>
        NormalizeFileCoreAsync(sourcePath, id, rejectMultipleFrames: false, cancellationToken);

    public Task<StoredImage> NormalizeStaticFileAsync(
        string sourcePath,
        Guid? id = null,
        CancellationToken cancellationToken = default) =>
        NormalizeFileCoreAsync(sourcePath, id, rejectMultipleFrames: true, cancellationToken);

    private async Task<StoredImage> NormalizeFileCoreAsync(
        string sourcePath,
        Guid? id,
        bool rejectMultipleFrames,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => SaveEncodedImage(bytes, id, cancellationToken, rejectMultipleFrames), cancellationToken).ConfigureAwait(false);
    }

    public Task<StoredImage> NormalizeBitmapAsync(
        BitmapSource bitmap,
        Guid? id = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        cancellationToken.ThrowIfCancellationRequested();
        var frozenBitmap = FreezeForBackgroundUse(bitmap);
        return Task.Run(
            () => NormalizeBitmapCore(frozenBitmap, id, cancellationToken),
            cancellationToken);
    }

    public Task<StoredImage> NormalizeClipboardAsync(
        IReadOnlyList<ClipboardImageCandidate> candidates,
        Guid? id = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one clipboard image candidate is required.", nameof(candidates));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = candidates.ToArray();
        return Task.Run(
            () => NormalizeClipboardCore(snapshot, id, cancellationToken),
            cancellationToken);
    }

    public async Task RepairStoredImagesOnceAsync(
        IEnumerable<string> imagePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imagePaths);
        var markerPath = Path.Combine(_imagesDirectory, ".zero-alpha-repair-v1");
        if (File.Exists(markerPath))
        {
            return;
        }

        Directory.CreateDirectory(_imagesDirectory);
        var completed = true;
        foreach (var imagePath in imagePaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsManagedImagePath(imagePath) || !File.Exists(imagePath))
            {
                continue;
            }

            try
            {
                await RepairStoredImageAsync(imagePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                completed = false;
            }
        }

        if (!completed)
        {
            return;
        }

        var temporaryMarker = markerPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryMarker, "completed", cancellationToken).ConfigureAwait(false);
            File.Move(temporaryMarker, markerPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryMarker))
            {
                File.Delete(temporaryMarker);
            }
        }
    }

    private StoredImage NormalizeBitmapCore(
        BitmapSource bitmap,
        Guid? id,
        CancellationToken cancellationToken)
        => SaveBitmap(PrepareBitmap(bitmap).Bitmap, id, cancellationToken);

    private StoredImage NormalizeClipboardCore(
        IReadOnlyList<ClipboardImageCandidate> candidates,
        Guid? id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selections = new List<ClipboardSelection>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Bitmap is not null)
            {
                var prepared = PrepareBitmap(candidate.Bitmap);
                if (prepared.HasPixelData)
                {
                    selections.Add(new ClipboardSelection(
                        candidate,
                        checked((long)prepared.Bitmap.PixelWidth * prepared.Bitmap.PixelHeight),
                        prepared.Bitmap));
                }

                continue;
            }

            try
            {
                var information = NativeImageCodec.Decode(candidate.EncodedBytes.ToArray());
                if (information.Bitmap.PixelWidth > 0 && information.Bitmap.PixelHeight > 0)
                {
                    selections.Add(new ClipboardSelection(
                        candidate,
                        checked((long)information.Bitmap.PixelWidth * information.Bitmap.PixelHeight),
                        null));
                }
            }
            catch (Exception exception) when (exception is IOException or NotSupportedException or ArgumentException)
            {
                // Another clipboard representation may still contain the same image.
            }
        }

        var selected = selections
            .OrderByDescending(selection => selection.PixelArea)
            .ThenByDescending(selection => selection.PreparedBitmap is not null)
            .FirstOrDefault()
            ?? throw new InvalidDataException("Clipboard does not contain a usable image representation.");

        return selected.PreparedBitmap is not null
            ? SaveBitmap(selected.PreparedBitmap, id, cancellationToken)
            : SaveEncodedImage(selected.Candidate.EncodedBytes, id, cancellationToken);
    }

    private StoredImage SaveBitmap(
        BitmapSource bitmap,
        Guid? id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stored = CreateDestination(id ?? Guid.NewGuid());
        var temporaryPath = stored.AbsolutePath + ".tmp";
        Directory.CreateDirectory(_imagesDirectory);

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                encoder.Save(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, stored.AbsolutePath, overwrite: false);
            return stored;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private StoredImage SaveEncodedImage(
        ReadOnlyMemory<byte> encodedBytes,
        Guid? id,
        CancellationToken cancellationToken,
        bool rejectMultipleFrames = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stored = CreateDestination(id ?? Guid.NewGuid());
        var temporaryPath = stored.AbsolutePath + ".tmp";
        Directory.CreateDirectory(_imagesDirectory);

        try
        {
            var decoded = NativeImageCodec.Decode(encodedBytes.ToArray());
            if (rejectMultipleFrames && decoded.FrameCount != 1) { throw new InvalidDataException("只接受单帧静态图片。"); }
            if (decoded.FrameCount == 1 && encodedBytes.Span.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            {
                File.WriteAllBytes(temporaryPath, encodedBytes.ToArray());
            }
            else
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(decoded.Bitmap));
                using var stream = File.Create(temporaryPath);
                encoder.Save(stream);
            }
            File.Move(temporaryPath, stored.AbsolutePath, overwrite: false);
            return stored;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task RepairStoredImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        var decoded = NativeImageCodec.Decode(await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false));
        var image = decoded.Bitmap.Format == System.Windows.Media.PixelFormats.Bgra32 ? decoded.Bitmap :
            new FormatConvertedBitmap(decoded.Bitmap, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var stride = checked(image.PixelWidth * 4);
        var pixels = new byte[checked(stride * image.PixelHeight)];
        image.CopyPixels(pixels, stride, 0);
        var allAlphaZero = true;
        var hasNonZeroRgb = false;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            allAlphaZero &= pixels[offset + 3] == 0;
            hasNonZeroRgb |= pixels[offset] != 0 || pixels[offset + 1] != 0 || pixels[offset + 2] != 0;
        }

        if (!allAlphaZero || !hasNonZeroRgb)
        {
            return;
        }

        for (var offset = 3; offset < pixels.Length; offset += 4) { pixels[offset] = 255; }

        var temporaryPath = imagePath + ".repair.tmp";
        try
        {
            var bitmap = BitmapSource.Create(image.PixelWidth, image.PixelHeight, image.DpiX, image.DpiY,
                System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(temporaryPath)) { encoder.Save(stream); }
            File.Move(temporaryPath, imagePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static BitmapSource FreezeForBackgroundUse(BitmapSource bitmap)
    {
        if (bitmap.IsFrozen)
        {
            return bitmap;
        }

        if (!bitmap.CanFreeze)
        {
            throw new InvalidOperationException("Clipboard bitmap cannot be frozen for background processing.");
        }

        var clone = bitmap.CloneCurrentValue();
        clone.Freeze();
        return clone;
    }

    private static PreparedBitmap PrepareBitmap(BitmapSource bitmap)
    {
        BitmapSource converted = bitmap;
        if (bitmap.Format != System.Windows.Media.PixelFormats.Bgra32)
        {
            converted = new FormatConvertedBitmap(
                bitmap,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                0);
            converted.Freeze();
        }

        var stride = checked(converted.PixelWidth * 4);
        var pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);

        var allAlphaZero = true;
        var hasNonZeroRgb = false;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            hasNonZeroRgb |= pixels[offset] != 0 || pixels[offset + 1] != 0 || pixels[offset + 2] != 0;
            allAlphaZero &= pixels[offset + 3] == 0;
        }

        if (allAlphaZero && hasNonZeroRgb)
        {
            for (var offset = 3; offset < pixels.Length; offset += 4)
            {
                pixels[offset] = byte.MaxValue;
            }
        }

        var result = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            converted.DpiX,
            converted.DpiY,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        result.Freeze();
        return new PreparedBitmap(result, !allAlphaZero || hasNonZeroRgb);
    }

    private bool IsManagedImagePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var allowedRoot = Path.GetFullPath(_imagesDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ClipboardSelection(
        ClipboardImageCandidate Candidate,
        long PixelArea,
        BitmapSource? PreparedBitmap);

    private readonly record struct PreparedBitmap(BitmapSource Bitmap, bool HasPixelData);

    private StoredImage CreateDestination(Guid id)
    {
        var fileName = $"{id:N}.png";
        return new StoredImage(
            id,
            $"images/{fileName}",
            Path.Combine(_imagesDirectory, fileName));
    }
}
