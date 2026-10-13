namespace ImageDiff.Core;

public sealed class DiffResult
{
    internal DiffResult(int width, int height, bool[] mask, long differentPixels, int maxChannelDelta, bool sizeMismatch)
    {
        Width = width;
        Height = height;
        Mask = mask;
        DifferentPixels = differentPixels;
        MaxChannelDelta = maxChannelDelta;
        SizeMismatch = sizeMismatch;
    }

    /// <summary>Width of the compared area (the larger of the two image widths).</summary>
    public int Width { get; }

    /// <summary>Height of the compared area (the larger of the two image heights).</summary>
    public int Height { get; }

    /// <summary>One entry per pixel, row-major; true where the images differ.</summary>
    public bool[] Mask { get; }

    public long DifferentPixels { get; }

    public long TotalPixels => (long)Width * Height;

    /// <summary>Largest single-channel difference found where the images overlap.</summary>
    public int MaxChannelDelta { get; }

    public bool SizeMismatch { get; }

    public bool AreIdentical => DifferentPixels == 0;

    public double DifferentFraction => TotalPixels == 0 ? 0 : (double)DifferentPixels / TotalPixels;

    public string Summary
    {
        get
        {
            if (AreIdentical)
                return "Images are identical.";
            string text = $"{DifferentPixels:N0} of {TotalPixels:N0} pixels differ ({DifferentFraction:P2}), max channel difference {MaxChannelDelta}.";
            return SizeMismatch ? text + " Image sizes differ." : text;
        }
    }
}
