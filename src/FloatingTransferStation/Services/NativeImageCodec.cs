using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace FloatingTransferStation.Services;

public readonly record struct NativeDecodedImage(BitmapSource Bitmap, int FrameCount);

public static class NativeImageCodec
{
    public static int ReadFrameCount(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[12];
        stream.ReadExactly(signature);
        stream.Position = 0;
        if (signature[..4].SequenceEqual("RIFF"u8) && signature[8..].SequenceEqual("WEBP"u8))
        {
            using var managed = new SKManagedStream(stream, disposeManagedStream: false);
            using var codec = SKCodec.Create(managed) ?? throw new InvalidDataException("WebP 图片内容无效。");
            return Math.Max(1, codec.FrameCount);
        }
        // Hover validation only reads frame metadata. Full pixel decoding stays on
        // the background import path, not the synchronous Windows drag-over event.
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var count = decoder.Frames.Count;
        if (signature[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            stream.Position = 8;
            Span<byte> header = stackalloc byte[8];
            while (stream.Position <= stream.Length - 12)
            {
                stream.ReadExactly(header);
                var length = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
                if (length > stream.Length - stream.Position - 4) { throw new InvalidDataException("PNG 数据块不完整。"); }
                if (header[4..].SequenceEqual("acTL"u8) && length == 8)
                {
                    Span<byte> frames = stackalloc byte[4]; stream.ReadExactly(frames);
                    var value = BinaryPrimitives.ReadUInt32BigEndian(frames);
                    return value is > 0 and <= int.MaxValue ? (int)value : throw new InvalidDataException("PNG 帧数无效。");
                }
                stream.Seek(length + 4L, SeekOrigin.Current);
            }
        }
        return count;
    }

    public static NativeDecodedImage Decode(byte[] bytes)
    {
        try { return DecodeCore(bytes); }
        catch (Exception error) when (error is NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException("图片内容无效或格式不受支持。", error);
        }
    }

    private static NativeDecodedImage DecodeCore(byte[] bytes)
    {
        if (bytes.Length < 12) { throw new InvalidDataException("图片内容无效。"); }
        var webp = bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8);
        if (webp)
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("WebP 图片内容无效。");
            if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 40_000_000)
            {
                throw new InvalidDataException("图片尺寸过大，请缩小后导入。");
            }
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels());
            if (result != SKCodecResult.Success) { throw new InvalidDataException("WebP 解码失败。"); }
            var native = BitmapSource.Create(info.Width, info.Height, 96, 96, PixelFormats.Bgra32, null,
                bitmap.Bytes, bitmap.RowBytes);
            native.Freeze();
            return new NativeDecodedImage(native, Math.Max(1, codec.FrameCount));
        }
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = decoder.Frames[0];
        image.Freeze();
        var count = decoder.Frames.Count;
        if (bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            for (var offset = 8; offset <= bytes.Length - 12;)
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
                if (length > (uint)(bytes.Length - offset - 12)) { throw new InvalidDataException("PNG 数据块不完整。"); }
                if (bytes.AsSpan(offset + 4, 4).SequenceEqual("acTL"u8) && length == 8)
                {
                    var frames = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8, 4));
                    if (frames == 0 || frames > int.MaxValue) { throw new InvalidDataException("PNG 帧数无效。"); }
                    count = (int)frames;
                }
                offset += checked((int)length + 12);
            }
        }
        return new NativeDecodedImage(image, count);
    }
}
