using S6.SnapVault.Models;

namespace S6.SnapVault.Configuration;

/// <summary>
/// Configures entity lifecycle events for cascade deletes and other side effects
/// </summary>
public static class EntityLifecycleConfiguration
{
    public static void Configure()
    {
        ConfigurePhotoAssetLifecycle();
    }

    private static void ConfigurePhotoAssetLifecycle()
    {
        // Cascade delete: When a PhotoAsset is deleted, delete related thumbnails and galleries
        PhotoAsset.Events.BeforeRemove(async ctx =>
        {
            var photo = ctx.Current;
            var ct = ctx.CancellationToken;

            // Delete thumbnail if it exists
            if (!string.IsNullOrEmpty(photo.ThumbnailMediaId))
            {
                var thumbnail = await PhotoThumbnail.Get(photo.ThumbnailMediaId, ct);
                if (thumbnail != null)
                {
                    await thumbnail.Delete(ct);
                }
            }

            // Delete gallery image if it exists
            if (!string.IsNullOrEmpty(photo.GalleryMediaId))
            {
                var gallery = await PhotoGallery.Get(photo.GalleryMediaId, ct);
                if (gallery != null)
                {
                    await gallery.Delete(ct);
                }
            }

            // Continue with the removal
            return Koan.Data.Core.Events.EntityEventResult.Continue();
        });
    }
}
