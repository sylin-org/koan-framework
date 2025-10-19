/**
 * Operation Utilities
 * Common operation patterns with UI feedback and data reloading
 */

/**
 * Execute operation with toast feedback and optional data reload
 * Standardizes the pattern: try operation → show success → reload data → clear selection
 *
 * @param {Function} operation - Async operation to execute (should return result or throw error)
 * @param {object} options - Options for feedback and reloading
 * @returns {Promise<any>} - Operation result
 *
 * @example
 * await executeWithFeedback(
 *   () => api.post('/api/photos/bulk/delete', { photoIds }),
 *   {
 *     successMessage: 'Deleted 5 photos',
 *     errorMessage: 'Failed to delete photos',
 *     successIcon: '🗑️',
 *     reloadPhotos: true,
 *     clearSelection: true,
 *     toast: app.components.toast,
 *     app: app
 *   }
 * );
 */
export async function executeWithFeedback(operation, options = {}) {
  const {
    successMessage = null,
    errorMessage = 'Operation failed',
    successIcon = '✓',
    errorIcon = '⚠️',
    successDuration = 2000,
    errorDuration = 3000,
    reloadPhotos = false,
    reloadCurrentView = false,
    reloadCollections = false,
    clearSelection = false,
    toast = null,
    app = null
  } = options;

  try {
    const result = await operation();

    // Show success message
    if (successMessage && toast) {
      toast.show(successMessage, { icon: successIcon, duration: successDuration });
    }

    // Reload data as needed
    if (reloadPhotos && app) {
      await app.loadPhotos();
    }

    if (reloadCurrentView && app?.components.collectionView) {
      // Reload current view (preserves context: all photos, favorites, or collection)
      await app.components.collectionView.loadPhotos();
    }

    if (reloadCollections && app?.components.collectionsSidebar) {
      await app.components.collectionsSidebar.loadCollections();
      app.components.collectionsSidebar.render();
    }

    // Clear selection
    if (clearSelection && app) {
      app.clearSelection();
    }

    return result;
  } catch (error) {
    console.error('[Operation Failed]', error);

    // Show error message
    if (toast) {
      toast.show(errorMessage, { icon: errorIcon, duration: errorDuration });
    }

    throw error;
  }
}

/**
 * Execute multiple operations in parallel with combined feedback
 * Useful for batch operations where individual failures shouldn't stop others
 *
 * @param {Function[]} operations - Array of async operations
 * @param {object} options - Options for feedback
 * @returns {Promise<{successful: number, failed: number, results: any[]}>}
 *
 * @example
 * const { successful, failed } = await executeParallel(
 *   photoIds.map(id => () => api.delete(`/api/photos/${id}`)),
 *   {
 *     successMessage: count => `Deleted ${count} photos`,
 *     errorMessage: 'Some deletions failed',
 *     toast: app.components.toast
 *   }
 * );
 */
export async function executeParallel(operations, options = {}) {
  const {
    successMessage = null,
    errorMessage = null,
    successIcon = '✓',
    errorIcon = '⚠️',
    toast = null
  } = options;

  const results = await Promise.allSettled(
    operations.map(op => op())
  );

  const successful = results.filter(r => r.status === 'fulfilled').length;
  const failed = results.filter(r => r.status === 'rejected').length;

  if (toast) {
    if (failed === 0 && successMessage) {
      const message = typeof successMessage === 'function'
        ? successMessage(successful)
        : successMessage;
      toast.show(message, { icon: successIcon, duration: 2000 });
    } else if (failed > 0 && errorMessage) {
      const message = typeof errorMessage === 'function'
        ? errorMessage(failed, successful)
        : errorMessage;
      toast.show(message, { icon: errorIcon, duration: 3000 });
    }
  }

  return {
    successful,
    failed,
    results: results.map(r => r.status === 'fulfilled' ? r.value : null)
  };
}
