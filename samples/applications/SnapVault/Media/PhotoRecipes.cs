using Koan.Media.Abstractions.Recipes;

namespace SnapVault.Media;

/// <summary>
/// On-demand image recipes rendered from each photo's single stored original. Koan.Media.Web serves them at
/// <c>GET /media/{photoId}/{name}</c> through access-scoped <c>PhotoAsset</c> resolution; the seedless route
/// returns the original.
///
/// <para>Recipes are discovered automatically. Their names are global slugs and avoid the reserved
/// format shortcuts (<c>jpeg/png/webp/gif/...</c>). The engine auto-orients by default (no orient step
/// needed). <c>EncodeAs("jpeg")</c> pins JPEG at <c>Quality.Web</c> (80).</para>
/// </summary>
public static class PhotoRecipes
{
    /// <summary>1200px web view — the lightbox's first frame and the AI vision source. Aspect-preserved JPEG.</summary>
    [MediaRecipe("gallery", Description = "1200px web view, JPEG")]
    public static MediaRecipe Gallery() => MediaRecipe.New().ResizeFit(1200, 1200).EncodeAs("jpeg");

    /// <summary>300px grid tile — the hot masonry path. Aspect-preserved JPEG.</summary>
    [MediaRecipe("masonry", Description = "300px masonry grid tile, JPEG")]
    public static MediaRecipe Masonry() => MediaRecipe.New().ResizeFit(300, 300).EncodeAs("jpeg");

    /// <summary>600px grid tile for retina / 4K displays. Aspect-preserved JPEG.</summary>
    [MediaRecipe("retina", Description = "600px retina/4K grid tile, JPEG")]
    public static MediaRecipe Retina() => MediaRecipe.New().ResizeFit(600, 600).EncodeAs("jpeg");

}
