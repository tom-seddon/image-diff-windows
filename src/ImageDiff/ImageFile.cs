using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageDiff.Core;

namespace ImageDiff;

/// <summary>Reads and writes images using the Windows imaging codecs (PNG, JPEG, BMP, GIF, TIFF, ICO, WMP and any installed WIC codecs).</summary>
public static class ImageFile
{
    public const string OpenFilter =
        "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.jxr;*.wdp;*.webp;*.heic|All files|*.*";

    public static PixelImage Load(string path)
    {
        using var stream = File.OpenRead(path);
        // Ignore colour profiles so the comparison sees the stored pixel values.
        var decoder = BitmapDecoder.Create(stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        if (frame.Format != PixelFormats.Bgra32)
            frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        int width = frame.PixelWidth, height = frame.PixelHeight;
        var pixels = new byte[width * height * 4];
        frame.CopyPixels(pixels, width * 4, 0);
        return new PixelImage(width, height, pixels);
    }

    /// <summary>Wraps pixels in a frozen bitmap at 96 DPI, so one image pixel is one unit at 100% zoom.</summary>
    public static BitmapSource ToBitmapSource(PixelImage image)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Bgra, image.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    public static void SavePng(PixelImage image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(ToBitmapSource(image)));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
