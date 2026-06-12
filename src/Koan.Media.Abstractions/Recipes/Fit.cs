namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// CSS-aligned <c>object-fit</c> semantics for the shape stage.
/// See MEDIA-0004 §5.
/// </summary>
public enum Fit
{
    /// <summary>
    /// Fill the shape, crop overflow. Default when <c>crop</c> is set
    /// or both <c>w</c> and <c>h</c> are provided.
    /// </summary>
    Cover = 0,

    /// <summary>Fit source inside the shape; leave bg-filled space if aspects differ.</summary>
    Contain = 1,

    /// <summary>Stretch to fill, aspect broken. CSS default but rarely the right pick.</summary>
    Fill = 2,

    /// <summary>Like <see cref="Contain"/> but never upscales.</summary>
    ScaleDown = 3,

    /// <summary>No resize; honour source dimensions.</summary>
    None = 4,
}
