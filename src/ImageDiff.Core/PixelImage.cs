namespace ImageDiff.Core;

/// <summary>
/// A decoded image as tightly packed 32-bit BGRA pixels (4 bytes per pixel, no row padding).
/// </summary>
public sealed class PixelImage
{
    public PixelImage(int width, int height, byte[] bgra)
    {
        if (width < 0 || height < 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Dimensions must be non-negative.");
        if (bgra.Length != width * height * 4)
            throw new ArgumentException($"Expected {width * height * 4} bytes for a {width}x{height} image, got {bgra.Length}.", nameof(bgra));

        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    public int Stride => Width * 4;

    public static PixelImage Create(int width, int height) => new(width, height, new byte[width * height * 4]);
}
