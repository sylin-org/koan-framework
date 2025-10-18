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

            // Delete thumbnail entity + storage file if it exists
            if (!string.IsNullOrEmpty(photo.ThumbnailMediaId))
            {
                var thumbnail = await PhotoThumbnail.Get(photo.ThumbnailMediaId, ct);
                if (thumbnail != null)
                {
                    await thumbnail.Delete(ct);  // Delete storage file
                    await thumbnail.Remove(ct);  // Delete database entity
                }
            }

            // Delete masonry thumbnail entity + storage file if it exists
            if (!string.IsNullOrEmpty(photo.MasonryThumbnailMediaId))
            {
                var masonryThumb = await PhotoMasonryThumbnail.Get(photo.MasonryThumbnailMediaId, ct);
                if (masonryThumb != null)
                {
                    await masonryThumb.Delete(ct);  // Delete storage file
                    await masonryThumb.Remove(ct);  // Delete database entity
                }
            }

            // Delete retina thumbnail entity + storage file if it exists
            if (!string.IsNullOrEmpty(photo.RetinaThumbnailMediaId))
            {
                var retinaThumb = await PhotoRetinaThumbnail.Get(photo.RetinaThumbnailMediaId, ct);
                if (retinaThumb != null)
                {
                    await retinaThumb.Delete(ct);  // Delete storage file
                    await retinaThumb.Remove(ct);  // Delete database entity
                }
            }

            // Delete gallery image entity + storage file if it exists
            if (!string.IsNullOrEmpty(photo.GalleryMediaId))
            {
                var gallery = await PhotoGallery.Get(photo.GalleryMediaId, ct);
                if (gallery != null)
                {
                    await gallery.Delete(ct);  // Delete storage file
                    await gallery.Remove(ct);  // Delete database entity
                }
            }

            // Delete the main photo's storage file (entity will be removed by the Remove() call)
            await photo.Delete(ct);

            // Continue with the removal
            return Koan.Data.Core.Events.EntityEventResult.Proceed();
        });
    }
}
