namespace Koan.Media.Core.Pipeline;

/// <summary>The geometry of the two aspect-preserving resize modes.</summary>
internal static class ResizePlan
{
    /// <summary>The size that fits inside the box without changing the aspect.</summary>
    public static (int Width, int Height) Contain(int sourceWidth, int sourceHeight, int boxWidth, int boxHeight)
    {
        var scaleWidth = boxWidth / (float)sourceWidth;
        var scaleHeight = boxHeight / (float)sourceHeight;
        return sourceHeight / (float)sourceWidth < boxHeight / (float)boxWidth
            ? (boxWidth, Math.Max(1, (int)MathF.Round(sourceHeight * scaleWidth)))
            : (Math.Max(1, (int)MathF.Round(sourceWidth * scaleHeight)), boxHeight);
    }

    /// <summary>The size that covers the box without changing the aspect, and where the box sits inside it, centered.</summary>
    public static (int Width, int Height, int OffsetX, int OffsetY) Cover(int sourceWidth, int sourceHeight, int boxWidth, int boxHeight)
    {
        var scaleWidth = boxWidth / (float)sourceWidth;
        var scaleHeight = boxHeight / (float)sourceHeight;
        if (scaleHeight < scaleWidth)
        {
            var height = Math.Max(boxHeight, (int)MathF.Ceiling(sourceHeight * scaleWidth));
            return (boxWidth, height, 0, (height - boxHeight) / 2);
        }
        var width = Math.Max(boxWidth, (int)MathF.Ceiling(sourceWidth * scaleHeight));
        return (width, boxHeight, (width - boxWidth) / 2, 0);
    }
}
