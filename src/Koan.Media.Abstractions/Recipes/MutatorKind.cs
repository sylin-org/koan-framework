namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Flags declaring which classes of URL/builder overrides a
/// <see cref="MediaRecipe"/> accepts. Defaults to <see cref="None"/>
/// — recipe authors opt in explicitly. URL overrides outside the
/// declared allowlist return 400 with a hint naming the recipe.
/// </summary>
[Flags]
public enum MutatorKind
{
    None = 0,

    /// <summary><c>?w=</c>, <c>?h=</c>, <c>?width=</c>, <c>?height=</c>, <c>?dpr=</c>.</summary>
    Dimensions = 1 << 0,

    /// <summary><c>?format=</c>, <c>?f=</c>.</summary>
    Format = 1 << 1,

    /// <summary><c>?q=</c>, <c>?quality=</c>.</summary>
    Quality = 1 << 2,

    /// <summary><c>?frame=</c> — index into animated source.</summary>
    Frame = 1 << 3,

    /// <summary><c>?position=</c> — anchor for shape crops.</summary>
    Position = 1 << 4,

    /// <summary><c>?bg=</c>, <c>?bg-fallback=</c>, <c>?bg-blur=</c>.</summary>
    Background = 1 << 5,

    /// <summary><c>?crop=</c> / <c>?aspect=</c> — replaces recipe's shape slot.</summary>
    Crop = 1 << 6,

    /// <summary><c>?fit=</c> — replaces recipe's fit mode.</summary>
    Fit = 1 << 7,

    /// <summary><c>?overlay=</c>, <c>?overlay.N.*=</c>.</summary>
    Overlay = 1 << 8,

    /// <summary><c>?rotate=</c>, <c>?flip=</c>.</summary>
    Rotate = 1 << 9,

    /// <summary><c>?strip=</c> — metadata stripping selectors.</summary>
    Strip = 1 << 10,

    /// <summary>Convenience: <see cref="Dimensions"/> | <see cref="Format"/> | <see cref="Quality"/>.</summary>
    Common = Dimensions | Format | Quality,

    /// <summary>All mutators. Reserved for ad-hoc / open recipes; not recommended for production variants.</summary>
    All = Dimensions | Format | Quality | Frame | Position | Background | Crop | Fit | Overlay | Rotate | Strip,
}
