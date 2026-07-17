using Koan.Media.Abstractions.Recipes;

namespace SnapVault.Media;

/// <summary>
/// SnapVault's on-demand image recipes (D3 / MEDIA-0004). These declarations replace the legacy eager
/// derivative machinery wholesale: the 4 derivative entity types, the 3 <c>*MediaId</c> FK fields, the
/// ~50-line <c>MaterializeAsync</c> fan-out, the <c>BaseType</c> reflection hack, and the bespoke 5-action
/// <c>MediaController</c>. The framework <c>Koan.Media.Web</c> controller serves each at
/// <c>GET /media/{photoId}/{name}</c> from the single stored original — resolved access-scoped through
/// <c>PhotoAsset</c> (see the registered <see cref="Koan.Media.Web.Routing.MediaEntitySource{TEntity}"/>) —
/// rendering (and, per step 3b, caching) on demand. The untransformed original is <c>GET /media/{photoId}</c>
/// (seedless).
///
/// <para>Discovered Reference=Intent by the assembly scan (referencing <c>Koan.Media.Core</c>) — no
/// registration. Recipe names are GLOBAL slugs: they must stay unique app-wide and avoid the reserved
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

    // A square 150² "thumbnail" recipe is intentionally omitted: the SPA builds only gallery/masonry/retina
    // URLs (+ the seedless original), and no surface consumes a square crop today. If one appears, add:
    //   [MediaRecipe("thumbnail", Description = "150² square crop, JPEG")]
    //   public static MediaRecipe Thumbnail() => MediaRecipe.New().ResizeCover(150, 150).EncodeAs("jpeg");
    // NOTE: ResizeCover(150,150) — NOT Crop(Square).ResizeFit(150,150): ResizeFit's internal Shape(Contain)
    // occupies the single Shape slot and would drop a preceding Crop.
}
