namespace Koan.Media.Core.Model;

/// <summary>
/// Represents a rectangular region with pixel coordinates
/// </summary>
public readonly struct Rectangle
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public Rectangle(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public override string ToString() => $"Rectangle({X}, {Y}, {Width}, {Height})";
}

/// <summary>
/// Specifies anchor position for crop/resize operations
/// </summary>
public enum CropFrom
{
    Center,
    Top,
    Bottom,
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>
/// Specifies padding constraints for aspect ratio normalization
/// </summary>
public enum PadTo
{
    Square,           // 1:1
    Landscape_16_9,   // 16:9 letterbox
    Portrait_9_16,    // 9:16 pillarbox
    Landscape_4_3,    // 4:3 letterbox
    Portrait_3_4,     // 3:4 pillarbox
    Landscape_21_9    // 21:9 ultrawide
}
