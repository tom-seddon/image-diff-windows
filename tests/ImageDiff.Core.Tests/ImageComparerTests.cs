using ImageDiff.Core;

namespace ImageDiff.Core.Tests;

public class ImageComparerTests
{
    private static PixelImage Solid(int w, int h, byte b, byte g, byte r, byte a = 255)
    {
        var img = PixelImage.Create(w, h);
        for (int i = 0; i < img.Bgra.Length; i += 4)
        {
            img.Bgra[i] = b; img.Bgra[i + 1] = g; img.Bgra[i + 2] = r; img.Bgra[i + 3] = a;
        }
        return img;
    }

    [Fact]
    public void IdenticalImagesHaveNoDifferences()
    {
        var diff = ImageComparer.Compare(Solid(4, 3, 10, 20, 30), Solid(4, 3, 10, 20, 30));
        Assert.True(diff.AreIdentical);
        Assert.Equal(12, diff.TotalPixels);
        Assert.Equal(0, diff.MaxChannelDelta);
        Assert.Equal("Images are identical.", diff.Summary);
    }

    [Fact]
    public void ChangedPixelIsFoundAndMasked()
    {
        var left = Solid(4, 3, 0, 0, 0);
        var right = Solid(4, 3, 0, 0, 0);
        right.Bgra[(2 * 4 + 1) * 4 + 2] = 200; // red channel of pixel (1, 2)

        var diff = ImageComparer.Compare(left, right);

        Assert.Equal(1, diff.DifferentPixels);
        Assert.Equal(200, diff.MaxChannelDelta);
        Assert.True(diff.Mask[2 * 4 + 1]);
        Assert.False(diff.SizeMismatch);
    }

    [Fact]
    public void AlphaDifferencesCount()
    {
        var diff = ImageComparer.Compare(Solid(2, 2, 5, 5, 5, 255), Solid(2, 2, 5, 5, 5, 0));
        Assert.Equal(4, diff.DifferentPixels);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(9, 4)]
    [InlineData(10, 0)]
    [InlineData(255, 0)]
    public void ToleranceIgnoresSmallDifferences(int tolerance, int expected)
    {
        var diff = ImageComparer.Compare(Solid(2, 2, 100, 100, 100), Solid(2, 2, 110, 95, 100), tolerance);
        Assert.Equal(expected, diff.DifferentPixels);
        Assert.Equal(10, diff.MaxChannelDelta);
    }

    [Fact]
    public void ExtraAreaOfLargerImageCountsAsDifferent()
    {
        var diff = ImageComparer.Compare(Solid(2, 2, 1, 2, 3), Solid(3, 4, 1, 2, 3));
        Assert.Equal(3, diff.Width);
        Assert.Equal(4, diff.Height);
        Assert.True(diff.SizeMismatch);
        Assert.Equal(12 - 4, diff.DifferentPixels);
        Assert.False(diff.Mask[0]);
        Assert.True(diff.Mask[2]);       // (2, 0) only in right
        Assert.True(diff.Mask[3 * 3]);   // (0, 3) only in right
        Assert.EndsWith("Image sizes differ.", diff.Summary);
    }

    [Fact]
    public void InvalidToleranceThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageComparer.Compare(Solid(1, 1, 0, 0, 0), Solid(1, 1, 0, 0, 0), 256));
    }

    [Fact]
    public void HighlightPaintsDifferencesAndFadesTheRest()
    {
        var left = Solid(2, 1, 0, 0, 0);
        var right = Solid(2, 1, 0, 0, 0);
        right.Bgra[4] = 255; // pixel (1, 0)

        var diff = ImageComparer.Compare(left, right);
        var highlight = ImageComparer.RenderHighlight(diff, left, 0xFFFF00FF);

        // Unchanged black pixel becomes light grey.
        Assert.Equal(new byte[] { 170, 170, 170, 255 }, highlight.Bgra[0..4]);
        // Changed pixel is magenta (B=255, G=0, R=255, A=255).
        Assert.Equal(new byte[] { 255, 0, 255, 255 }, highlight.Bgra[4..8]);
    }

    [Fact]
    public void HighlightCoversAreaOutsideBackground()
    {
        var diff = ImageComparer.Compare(Solid(1, 1, 0, 0, 0), Solid(2, 1, 0, 0, 0));
        var highlight = ImageComparer.RenderHighlight(diff, Solid(1, 1, 0, 0, 0));
        Assert.Equal(2, highlight.Width);
        Assert.Equal(new byte[] { 255, 0, 255, 255 }, highlight.Bgra[4..8]);
    }
}
