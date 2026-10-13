using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FloatingTransferStation.Services;
using SkiaSharp;

namespace FloatingTransferStation.Tests.Imaging;

public readonly record struct Rgba32(byte R, byte G, byte B, byte A);

public static class Color
{
    public static Rgba32 Red => new(255, 0, 0, 255);
    public static Rgba32 Green => new(0, 128, 0, 255);
    public static Rgba32 CornflowerBlue => new(100, 149, 237, 255);
}

public sealed class GifEncoder;

public static class Image
{
    public static Image<Rgba32> Load(string path) => Load<Rgba32>(path);
    public static Image<T> Load<T>(string path) => Image<T>.FromBytes(File.ReadAllBytes(path));
    public static Task<Image<Rgba32>> LoadAsync(string path) => Task.FromResult(Load(path));
    public static Task<Image<T>> LoadAsync<T>(string path) => Task.FromResult(Load<T>(path));
    public static ImageInformation Identify(string path)
    {
        var decoded = NativeImageCodec.Decode(File.ReadAllBytes(path));
        return new ImageInformation(Enumerable.Range(0, decoded.FrameCount).ToArray());
    }
}

public sealed record ImageInformation(IReadOnlyList<int> FrameMetadataCollection);

public sealed class ImageFrames
{
    public int Count { get; internal set; } = 1;
    public object RootFrame { get; } = new();
    public void AddFrame(object frame) => Count++;
}

public sealed class Image<T> : IDisposable
{
    private readonly byte[] _pixels;
    public int Width { get; }
    public int Height { get; }
    public ImageFrames Frames { get; } = new();

    public Image(int width, int height, Rgba32 color)
    {
        Width = width; Height = height;
        _pixels = new byte[width * height * 4];
        for (var offset = 0; offset < _pixels.Length; offset += 4)
        {
            _pixels[offset] = color.B; _pixels[offset + 1] = color.G; _pixels[offset + 2] = color.R; _pixels[offset + 3] = color.A;
        }
    }

    public Rgba32 this[int x, int y]
    {
        get { var offset = (y * Width + x) * 4; return new(_pixels[offset + 2], _pixels[offset + 1], _pixels[offset], _pixels[offset + 3]); }
    }

    internal static Image<T> FromBytes(byte[] bytes)
    {
        var decoded = NativeImageCodec.Decode(bytes);
        var bitmap = decoded.Bitmap.Format == PixelFormats.Bgra32 ? decoded.Bitmap : new FormatConvertedBitmap(decoded.Bitmap, PixelFormats.Bgra32, null, 0);
        var image = new Image<T>(bitmap.PixelWidth, bitmap.PixelHeight, default);
        bitmap.CopyPixels(image._pixels, bitmap.PixelWidth * 4, 0);
        image.Frames.Count = decoded.FrameCount;
        return image;
    }

    public void SaveAsPng(string path) => Save(path);
    public Task SaveAsPngAsync(string path) => SaveAsync(path);
    public Task SaveAsJpegAsync(string path) => SaveAsync(path);
    public Task SaveAsGifAsync(string path) => SaveAsync(path);
    public Task SaveAsPngAsync(Stream stream) { Encode(stream, "png"); return Task.CompletedTask; }
    public void SaveAsGif(Stream stream) => Encode(stream, "gif");
    public void Save(Stream stream, GifEncoder encoder) => Encode(stream, "gif");
    public void Save(string path) { using var stream = File.Create(path); Encode(stream, Path.GetExtension(path).TrimStart('.')); }
    public Task SaveAsync(string path) { Save(path); return Task.CompletedTask; }
    public void Dispose() { }

    private void Encode(Stream stream, string format)
    {
        if (format == "webp")
        {
            using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            System.Runtime.InteropServices.Marshal.Copy(_pixels, 0, bitmap.GetPixels(), _pixels.Length);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Webp, 100);
            var bytes = data.ToArray();
            stream.Write(Frames.Count == 1 ? bytes : AnimatedWebp(bytes));
            return;
        }
        BitmapEncoder encoder = format switch
        {
            "jpg" or "jpeg" => new JpegBitmapEncoder(),
            "gif" => new GifBitmapEncoder(),
            "bmp" => new BmpBitmapEncoder(),
            "tiff" or "tif" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
        var source = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, _pixels, Width * 4);
        var count = format == "png" ? 1 : Frames.Count;
        for (var i = 0; i < count; i++) { encoder.Frames.Add(BitmapFrame.Create(source)); }
        if (format == "png" && Frames.Count > 1)
        {
            using var png = new MemoryStream(); encoder.Save(png); stream.Write(AnimatedPng(png.ToArray()));
        }
        else { encoder.Save(stream); }
    }

    private byte[] AnimatedPng(byte[] png)
    {
        using var output = new MemoryStream(); output.Write(png.AsSpan(0, 8));
        var data = new List<byte[]>();
        byte[]? end = null;
        var sequence = 0u;
        for (var offset = 8; offset < png.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            var chunk = png.AsSpan(offset, length + 12).ToArray();
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT") { data.Add(png.AsSpan(offset + 8, length).ToArray()); }
            else if (type == "IEND") { end = chunk; }
            else { output.Write(chunk); }
            offset += length + 12;
        }
        var control = new byte[8]; BinaryPrimitives.WriteUInt32BigEndian(control, (uint)Frames.Count);
        WritePngChunk(output, "acTL", control);
        for (var frame = 0; frame < Frames.Count; frame++)
        {
            var header = new byte[26]; BinaryPrimitives.WriteUInt32BigEndian(header, sequence++);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)Width);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), (uint)Height);
            header[21] = 1; header[23] = 10;
            WritePngChunk(output, "fcTL", header);
            foreach (var payload in data)
            {
                if (frame == 0) { WritePngChunk(output, "IDAT", payload); }
                else
                {
                    var content = new byte[payload.Length + 4]; BinaryPrimitives.WriteUInt32BigEndian(content, sequence++);
                    payload.CopyTo(content, 4); WritePngChunk(output, "fdAT", content);
                }
            }
        }
        output.Write(end!); return output.ToArray();
    }

    private static void WritePngChunk(Stream stream, string type, byte[] bytes)
    {
        var prefix = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)bytes.Length); stream.Write(prefix);
        var tag = System.Text.Encoding.ASCII.GetBytes(type); stream.Write(tag); stream.Write(bytes);
        var crc = uint.MaxValue;
        foreach (var value in tag.Concat(bytes))
        {
            crc ^= value; for (var i = 0; i < 8; i++) { crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0u); }
        }
        BinaryPrimitives.WriteUInt32BigEndian(prefix, crc ^ uint.MaxValue); stream.Write(prefix);
    }

    private byte[] AnimatedWebp(byte[] encoded)
    {
        using var body = new MemoryStream(); body.Write("WEBP"u8);
        var extended = new byte[10]; extended[0] = 2;
        Write24(extended.AsSpan(4), Width - 1); Write24(extended.AsSpan(7), Height - 1);
        WriteRiffChunk(body, "VP8X", extended); WriteRiffChunk(body, "ANIM", new byte[6]);
        for (var frame = 0; frame < Frames.Count; frame++)
        {
            using var contents = new MemoryStream(); var header = new byte[16];
            Write24(header.AsSpan(6), Width - 1); Write24(header.AsSpan(9), Height - 1); Write24(header.AsSpan(12), 100); header[15] = 2;
            contents.Write(header);
            for (var offset = 12; offset + 8 <= encoded.Length;)
            {
                var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(offset + 4, 4)));
                var tag = System.Text.Encoding.ASCII.GetString(encoded, offset, 4);
                if (tag is "VP8 " or "VP8L" or "ALPH") { contents.Write(encoded.AsSpan(offset, size + 8 + (size & 1))); }
                offset += size + 8 + (size & 1);
            }
            WriteRiffChunk(body, "ANMF", contents.ToArray());
        }
        using var result = new MemoryStream(); result.Write("RIFF"u8);
        var length = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)body.Length); result.Write(length); body.Position = 0; body.CopyTo(result);
        return result.ToArray();
    }

    private static void Write24(Span<byte> bytes, int value) { bytes[0] = (byte)value; bytes[1] = (byte)(value >> 8); bytes[2] = (byte)(value >> 16); }
    private static void WriteRiffChunk(Stream stream, string tag, byte[] data)
    {
        stream.Write(System.Text.Encoding.ASCII.GetBytes(tag)); var length = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)data.Length);
        stream.Write(length); stream.Write(data); if ((data.Length & 1) != 0) { stream.WriteByte(0); }
    }
}
