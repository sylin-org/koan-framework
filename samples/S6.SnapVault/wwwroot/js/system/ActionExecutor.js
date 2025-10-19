/**
 * Action Executor
 * Uniform execution engine for all user actions
 * Handles confirmation, feedback, refresh, and error handling
 */

import { getAction } from './ActionRegistry.js';
import { Button } from './Button.js';

export class ActionExecutor {
  constructor(app) {
    this.app = app;
  }

  /**
   * Execute an action
   * @param {string} actionId - Action ID from registry
   * @param {any} context - Action context (photoId, photoIds, collection, etc.)
   * @param {object} options - Execution options
   * @returns {Promise<any>} Action result
   */
  async execute(actionId, context = null, options = {}) {
    console.log(`[ActionExecutor.execute] Starting: "${actionId}"`, { context });

    const action = getAction(actionId);
    if (!action) {
      console.error(`[ActionExecutor] Action "${actionId}" not found`);
      return null;
    }

    // Check if action is available
    if (!action.isAvailable(this.app, context)) {
      console.warn(`[ActionExecutor] Action "${actionId}" not available in current context`);
      return null;
    }

    // Handle confirmation if required
    if (action.requiresConfirmation) {
      const confirmed = action.getConfirmation
        ? action.getConfirmation(context)
        : confirm(`Are you sure you want to ${action.label.toLowerCase()}?`);

      if (!confirmed) {
        return null; // User cancelled
      }
    }

    try {
      let result;
      const isBulk = Array.isArray(context);

      // Execute action
      if (isBulk && action.executeBulk) {
        result = await action.executeBulk(this.app, context);
      } else if (!isBulk && action.execute) {
        result = await action.execute(this.app, context);
      } else {
        throw new Error(`Action "${actionId}" does not support ${isBulk ? 'bulk' : 'single'} execution`);
      }

      // Show success feedback
      if (action.feedback) {
        const message = isBulk
          ? (action.feedback.bulk ? action.feedback.bulk(context.length) : null)
          : (action.feedback.single ? (typeof action.feedback.single === 'function' ? action.feedback.single(result) : action.feedback.single) : null);

        if (message) {
          this.app.components.toast.show(message, {
            icon: action.feedback.icon || '✓',
            duration: 2000
          });
        }
      }

      // Handle refresh strategies
      if (action.refresh) {
        await this.handleRefresh(action.refresh);
      }

      return result;
    } catch (error) {
      console.error(`[ActionExecutor] Action "${actionId}" failed:`, error);

      // Show error feedback
      if (action.feedback && action.feedback.error) {
        this.app.components.toast.show(action.feedback.error, {
          icon: '⚠️',
          duration: 3000
        });
      }

      throw error;
    }
  }

  /**
   * Execute action for selected photos
   * Automatically gets selected photo IDs from app state
   * @param {string} actionId - Action ID from registry
   * @returns {Promise<any>} Action result
   */
  async executeForSelection(actionId) {
    const photoIds = this.getSelectedPhotoIds();
    if (!photoIds || photoIds.length === 0) {
      this.app.components.toast.show('No photos selected', {
        icon: 'ℹ️',
        duration: 2000
      });
      return null;
    }

    return await this.execute(actionId, photoIds);
  }

  /**
   * Execute action for current collection
   * @param {string} actionId - Action ID from registry
   * @returns {Promise<any>} Action result
   */
  async executeForCollection(actionId) {
    const { viewState } = this.app.components.collectionView;
    if (viewState.type !== 'collection') {
      console.warn('[ActionExecutor] Not in collection view');
      return null;
    }

    return await this.execute(actionId, viewState.collection);
  }

  /**
   * Create a button for an action
   * @param {string} actionId - Action ID from registry
   * @param {any} context - Action context (optional, uses selection if not provided)
   * @param {object} buttonOptions - Additional button options
   * @returns {HTMLButtonElement} Button element
   */
  createButton(actionId, context = null, buttonOptions = {}) {
    const action = getAction(actionId);
    if (!action) {
      console.error(`[ActionExecutor] Action "${actionId}" not found`);
      return null;
    }

    // Determine if action is available
    const isAvailable = action.isAvailable(this.app, context);

    return Button.create({
      label: action.label,
      icon: action.icon,
      variant: action.variant || 'default',
      disabled: !isAvailable,
      onClick: async () => {
        console.log(`[ActionExecutor] Button clicked: "${actionId}"`, { context });
        try {
          if (context !== null) {
            await this.execute(actionId, context);
          } else {
            // Use selection context
            await this.executeForSelection(actionId);
          }
        } catch (error) {
          console.error(`[ActionExecutor] Button click failed for "${actionId}":`, error);
        }
      },
      ...buttonOptions
    });
  }

  /**
   * Create a group of action buttons
   * @param {Array<string|object>} actions - Array of action IDs or configs
   * @param {any} context - Shared context for all actions
   * @param {object} groupOptions - Button group options
   * @returns {HTMLDivElement} Button group element
   */
  createButtonGroup(actions, context = null, groupOptions = {}) {
    const { className = 'actions-grid' } = groupOptions;

    const buttons = actions
      .map(actionConfig => {
        const actionId = typeof actionConfig === 'string' ? actionConfig : actionConfig.id;
        const buttonOptions = (typeof actionConfig === 'object' && actionConfig.options) ? actionConfig.options : {};

        return this.createButton(actionId, context, buttonOptions);
      })
      .filter(btn => btn !== null); // Filter out unavailable actions

    // Create container and append already-created buttons
    const group = document.createElement('div');
    group.className = className;
    buttons.forEach(btn => group.appendChild(btn));

    return group;
  }

  /**
   * Get selected photo IDs from app state
   * @returns {Array<string>|null} Array of photo IDs or null if none selected
   */
  getSelectedPhotoIds() {
    if (!this.app.state.selectedPhotos || this.app.state.selectedPhotos.size === 0) {
      return null;
    }

    return Array.from(this.app.state.selectedPhotos);
  }

  /**
   * Handle refresh strategies
   * @param {object} refresh - Refresh configuration from action
   */
  async handleRefresh(refresh) {
    if (!refresh) return;

    // Reload current view
    if (refresh.reloadView && this.app.components.collectionView) {
      await this.app.components.collectionView.loadPhotos();
    }

    // Reload collections sidebar
    if (refresh.reloadCollections && this.app.components.collectionsSidebar) {
      await this.app.components.collectionsSidebar.loadCollections();
      this.app.components.collectionsSidebar.render();
    }

    // Clear selection
    if (refresh.clearSelection && this.app.clearSelection) {
      this.app.clearSelection();
    }

    // Navigate to all photos (after collection delete)
    if (refresh.navigateToAllPhotos && this.app.components.collectionView) {
      await this.app.components.collectionView.setView('all-photos');
    }

    // Update specific photo in state
    if (refresh.updatePhoto && refresh.photoId) {
      // Photo update is handled in action execute
    }
  }

  /**
   * Get label for action with dynamic count
   * Useful for buttons that show count (e.g., "Download (5)")
   * @param {string} actionId - Action ID
   * @param {number} count - Number of items
   * @returns {string} Label with count
   */
  getLabelWithCount(actionId, count) {
    const action = getAction(actionId);
    if (!action) return '';

    return `${action.label}${count > 0 ? ` (${count})` : ''}`;
  }
}
