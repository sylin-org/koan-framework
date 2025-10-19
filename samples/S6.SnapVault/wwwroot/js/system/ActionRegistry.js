/**
 * Action Registry
 * Centralized action definitions for all user operations
 * Single source of truth for permissions, UI generation, and execution logic
 */

export const ActionRegistry = {
  // ==================== Photo Actions ====================

  'photo.favorite': {
    id: 'photo.favorite',
    label: 'Add to Favorites',
    icon: 'star',
    hotkey: 'f',
    contexts: ['grid', 'lightbox', 'selection'],
    variant: 'default',

    // Check if action is available in current context
    isAvailable: (app, context) => {
      return true; // Always available
    },

    // Execute for single photo
    async execute(app, photoId) {
      const response = await app.api.post(`/api/photos/${photoId}/favorite`);

      // Update photo state
      if (app.state.photos) {
        const photo = app.state.photos.find(p => p.id === photoId);
        if (photo) photo.isFavorite = response.isFavorite;
      }

      return response;
    },

    // Execute for multiple photos
    async executeBulk(app, photoIds) {
      const response = await app.api.post('/api/photos/bulk/favorite', {
        photoIds: photoIds,
        isFavorite: true
      });

      // Update photo state optimistically
      if (app.state.photos) {
        photoIds.forEach(photoId => {
          const photo = app.state.photos.find(p => p.id === photoId);
          if (photo) photo.isFavorite = true;
        });
      }

      return response;
    },

    feedback: {
      single: (result) => result.isFavorite ? 'Added to favorites' : 'Removed from favorites',
      bulk: (count) => `Added ${count} ${count === 1 ? 'photo' : 'photos'} to favorites`,
      error: 'Failed to add to favorites',
      icon: '⭐'
    },

    refresh: {
      clearSelection: true
      // No view reload - state updated optimistically
    }
  },

  'photo.unfavorite': {
    id: 'photo.unfavorite',
    label: 'Remove from Favorites',
    icon: 'star',
    contexts: ['favorites'],
    variant: 'default',

    isAvailable: (app, context) => {
      const { viewState } = app.components.collectionView;
      return viewState.type === 'favorites';
    },

    async executeBulk(app, photoIds) {
      return await app.api.post('/api/photos/bulk/favorite', {
        photoIds: photoIds,
        isFavorite: false
      });
    },

    feedback: {
      bulk: (count) => `Removed ${count} ${count === 1 ? 'photo' : 'photos'} from favorites`,
      error: 'Failed to update favorites',
      icon: '⭐'
    },

    refresh: {
      reloadView: true,
      clearSelection: true
    }
  },

  'photo.download': {
    id: 'photo.download',
    label: 'Download',
    icon: 'download',
    hotkey: 'd',
    contexts: ['grid', 'lightbox', 'selection'],
    variant: 'default',

    isAvailable: () => true,

    execute(app, photoId) {
      window.open(`/api/photos/${photoId}/download`, '_blank');
      return Promise.resolve({ downloaded: true });
    },

    executeBulk(app, photoIds) {
      photoIds.forEach(photoId => {
        window.open(`/api/photos/${photoId}/download`, '_blank');
      });
      return Promise.resolve({ downloaded: photoIds.length });
    },

    feedback: {
      single: 'Download started',
      bulk: (count) => `Downloading ${count} ${count === 1 ? 'photo' : 'photos'}`,
      icon: '⬇️'
    },

    refresh: {
      // No refresh needed
    }
  },

  'photo.delete': {
    id: 'photo.delete',
    label: 'Delete',
    icon: 'trash',
    hotkey: 'delete',
    contexts: ['grid', 'lightbox', 'selection'],
    variant: 'destructive',

    isAvailable: (app, context) => {
      // Not available in collection views (use remove instead)
      const { viewState } = app.components.collectionView;
      return viewState.type !== 'collection';
    },

    // Requires confirmation
    requiresConfirmation: true,
    getConfirmation: (count) => {
      const message = count === 1
        ? 'Delete this photo?'
        : `Delete ${count} photos?`;
      const detail = `This will delete the ${count === 1 ? 'photo' : 'photos'} and all thumbnails from storage.`;
      return confirm(`${message}\n\n${detail}\n\nThis action cannot be undone.`);
    },

    async executeBulk(app, photoIds) {
      return await app.api.post('/api/photos/bulk/delete', { photoIds });
    },

    feedback: {
      bulk: (count) => `Deleted ${count} ${count === 1 ? 'photo' : 'photos'}`,
      error: 'Failed to delete photos',
      icon: '🗑️'
    },

    refresh: {
      reloadView: true,
      clearSelection: true
    }
  },

  'photo.addToCollection': {
    id: 'photo.addToCollection',
    label: 'Add to Collection...',
    icon: 'folderPlus',
    contexts: ['selection'],
    variant: 'default',

    isAvailable: (app, context) => {
      // Not available when already in a collection
      const { viewState } = app.components.collectionView;
      return viewState.type !== 'collection';
    },

    async executeBulk(app, photoIds) {
      // TODO: Show collection picker dialog
      app.components.toast.show('Collection picker coming soon', {
        icon: 'ℹ️',
        duration: 2000
      });
      return Promise.resolve({ added: 0 });
    },

    feedback: {
      bulk: (count) => `Added ${count} ${count === 1 ? 'photo' : 'photos'} to collection`,
      error: 'Failed to add to collection'
    },

    refresh: {
      reloadView: true,
      clearSelection: true
    }
  },

  'photo.removeFromCollection': {
    id: 'photo.removeFromCollection',
    label: 'Remove from Collection',
    icon: 'x',
    contexts: ['collection'],
    variant: 'default',

    isAvailable: (app, context) => {
      const { viewState } = app.components.collectionView;
      return viewState.type === 'collection';
    },

    async executeBulk(app, photoIds) {
      const { viewState } = app.components.collectionView;
      if (viewState.type !== 'collection') {
        throw new Error('Not in collection view');
      }

      return await app.api.post(`/api/collections/${viewState.collection.id}/photos/remove`, {
        photoIds: photoIds
      });
    },

    feedback: {
      bulk: (count) => `Removed ${count} ${count === 1 ? 'photo' : 'photos'} from collection`,
      error: 'Failed to remove photos',
      icon: '✓'
    },

    refresh: {
      reloadView: true,
      reloadCollections: true,
      clearSelection: true
    }
  },

  'photo.analyzeAI': {
    id: 'photo.analyzeAI',
    label: 'Analyze with AI',
    icon: 'sparkles',
    contexts: ['selection'],
    variant: 'default',

    isAvailable: () => true,

    async executeBulk(app, photoIds) {
      // TODO: Implement AI analysis
      app.components.toast.show('AI analysis coming soon', {
        icon: '🤖',
        duration: 2000
      });
      return Promise.resolve({ analyzed: 0 });
    },

    feedback: {
      bulk: (count) => `Analyzing ${count} ${count === 1 ? 'photo' : 'photos'}`,
      icon: '🤖'
    },

    refresh: {}
  },

  // ==================== Collection Actions ====================

  'collection.rename': {
    id: 'collection.rename',
    label: 'Rename Collection',
    icon: 'edit',
    contexts: ['collection'],
    variant: 'default',

    isAvailable: (app, context) => {
      return app.components.collectionView?.viewState?.type === 'collection';
    },

    execute(app, collection) {
      // Trigger header title edit
      const titleElement = document.querySelector('.content-header .page-title');
      if (titleElement && titleElement.classList.contains('editable')) {
        titleElement.click();
      }
      return Promise.resolve({ renamed: true });
    },

    feedback: {
      // Feedback handled by title edit component
    },

    refresh: {}
  },

  'collection.duplicate': {
    id: 'collection.duplicate',
    label: 'Duplicate Collection',
    icon: 'copy',
    contexts: ['collection'],
    variant: 'default',

    isAvailable: (app) => {
      return app.components.collectionView?.viewState?.type === 'collection';
    },

    async execute(app, collection) {
      // TODO: Implement duplicate functionality
      app.components.toast.show('Duplicate collection coming soon', {
        icon: 'ℹ️',
        duration: 2000
      });
      return Promise.resolve({ duplicated: false });
    },

    feedback: {
      single: 'Collection duplicated',
      error: 'Failed to duplicate collection'
    },

    refresh: {
      reloadCollections: true
    }
  },

  'collection.export': {
    id: 'collection.export',
    label: 'Export Collection...',
    icon: 'download',
    contexts: ['collection'],
    variant: 'default',

    isAvailable: (app) => {
      return app.components.collectionView?.viewState?.type === 'collection';
    },

    async execute(app, collection) {
      // TODO: Implement export functionality
      app.components.toast.show('Export collection coming soon', {
        icon: 'ℹ️',
        duration: 2000
      });
      return Promise.resolve({ exported: false });
    },

    feedback: {
      single: 'Collection exported',
      error: 'Failed to export collection'
    },

    refresh: {}
  },

  'collection.delete': {
    id: 'collection.delete',
    label: 'Delete Collection',
    icon: 'trash',
    contexts: ['collection'],
    variant: 'destructive',

    isAvailable: (app) => {
      return app.components.collectionView?.viewState?.type === 'collection';
    },

    requiresConfirmation: true,
    getConfirmation: (collection) => {
      const photoCount = collection.photoCount || 0;
      const message = `Delete collection "${collection.name}"?`;
      const detail = photoCount > 0
        ? `This collection contains ${photoCount} ${photoCount === 1 ? 'photo' : 'photos'}. The photos will not be deleted, only the collection.`
        : 'This collection is empty.';
      return confirm(`${message}\n\n${detail}\n\nThis action cannot be undone.`);
    },

    async execute(app, collection) {
      // Delegate to collectionsSidebar
      if (app.components.collectionsSidebar) {
        await app.components.collectionsSidebar.deleteCollection(collection.id);
      }
      return Promise.resolve({ deleted: true });
    },

    feedback: {
      single: (collection) => `Collection "${collection.name}" deleted`,
      error: 'Failed to delete collection',
      icon: '🗑️'
    },

    refresh: {
      reloadCollections: true,
      navigateToAllPhotos: true
    }
  }
};

/**
 * Get action definition by ID
 */
export function getAction(actionId) {
  const action = ActionRegistry[actionId];
  if (!action) {
    console.warn(`[ActionRegistry] Action "${actionId}" not found`);
    return null;
  }
  return action;
}

/**
 * Get all actions available in a specific context
 */
export function getActionsForContext(app, context) {
  return Object.values(ActionRegistry)
    .filter(action =>
      action.contexts.includes(context) &&
      action.isAvailable(app, context)
    );
}

/**
 * Check if action is available
 */
export function isActionAvailable(app, actionId, context) {
  const action = getAction(actionId);
  if (!action) return false;
  return action.isAvailable(app, context);
}
