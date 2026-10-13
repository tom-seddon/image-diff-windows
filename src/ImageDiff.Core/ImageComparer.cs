namespace ImageDiff.Core;

public static class ImageComparer
{
    /// <summary>
    /// Compares two images pixel by pixel, aligned at the top-left corner.
    /// A pixel counts as different when any channel (B, G, R or A) differs by more than
    /// <paramref name="tolerance"/>. Where the images have different sizes, every pixel
    /// that only exists in one of them counts as different.
    /// </summary>
    public static DiffResult Compare(PixelImage left, PixelImage right, int tolerance = 0)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (tolerance < 0 || tolerance > 255)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Tolerance must be between 0 and 255.");

        int width = Math.Max(left.Width, right.Width);
        int height = Math.Max(left.Height, right.Height);
        int overlapW = Math.Min(left.Width, right.Width);
        int overlapH = Math.Min(left.Height, right.Height);

        var mask = new bool[width * height];
        long different = 0;
        int maxDelta = 0;

        byte[] a = left.Bgra, b = right.Bgra;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool diff;
                if (x < overlapW && y < overlapH)
                {
                    int ia = (y * left.Width + x) * 4;
                    int ib = (y * right.Width + x) * 4;
                    int d = Math.Abs(a[ia] - b[ib]);
                    d = Math.Max(d, Math.Abs(a[ia + 1] - b[ib + 1]));
                    d = Math.Max(d, Math.Abs(a[ia + 2] - b[ib + 2]));
                    d = Math.Max(d, Math.Abs(a[ia + 3] - b[ib + 3]));
                    if (d > maxDelta) maxDelta = d;
                    diff = d > tolerance;
                }
                else
                {
                    diff = true;
                }

                if (diff)
                {
                    mask[y * width + x] = true;
                    different++;
                }
            }
        }

        return new DiffResult(width, height, mask, different, maxDelta,
            sizeMismatch: left.Width != right.Width || left.Height != right.Height);
    }

    /// <summary>
    /// Builds a highlight image: <paramref name="background"/> shown faded and greyscale,
    /// with every differing pixel painted in <paramref name="highlightArgb"/> (0xAARRGGBB).
    /// </summary>
    public static PixelImage RenderHighlight(DiffResult diff, PixelImage background, uint highlightArgb = 0xFFFF00FF)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(background);

        byte hb = (byte)highlightArgb, hg = (byte)(highlightArgb >> 8), hr = (byte)(highlightArgb >> 16), ha = (byte)(highlightArgb >> 24);
        var output = PixelImage.Create(diff.Width, diff.Height);
        byte[] dst = output.Bgra, src = background.Bgra;

        for (int y = 0; y < diff.Height; y++)
        {
            for (int x = 0; x < diff.Width; x++)
            {
                int o = (y * diff.Width + x) * 4;
                if (diff.Mask[y * diff.Width + x])
                {
                    dst[o] = hb; dst[o + 1] = hg; dst[o + 2] = hr; dst[o + 3] = ha;
                    continue;
                }

                byte grey = 255;
                if (x < background.Width && y < background.Height)
                {
                    int i = (y * background.Width + x) * 4;
                    // Rec. 601 luma, composited over white, then faded towards white.
                    int luma = (src[i] * 29 + src[i + 1] * 150 + src[i + 2] * 77) >> 8;
                    int alpha = src[i + 3];
                    int over = (luma * alpha + 255 * (255 - alpha)) / 255;
                    grey = (byte)(over / 3 + 170);
                }

                dst[o] = grey; dst[o + 1] = grey; dst[o + 2] = grey; dst[o + 3] = 255;
            }
        }

        return output;
    }
}
