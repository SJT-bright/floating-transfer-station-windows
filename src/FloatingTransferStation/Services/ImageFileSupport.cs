namespace FloatingTransferStation.Services;

public static class ImageFileSupport
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    public static bool IsSupported(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Extensions.Contains(Path.GetExtension(path));

    public static bool IsSupportedStaticImageFile(string path)
    {
        if (!IsSupported(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            return NativeImageCodec.Decode(File.ReadAllBytes(path)).FrameCount == 1;
        }
        catch
        {
            return false;
        }
    }
}
