/**
 * PhotoSelection Component
 * Text-selection drag pattern for photo organization
 * Simple approach: Just monitor drops and check what's selected
 */

export class PhotoSelection {
  constructor(app) {
    this.app = app;
    this.selectedPhotoIds = [];
  }

  init() {
    // Monitor selection changes to update drop zone availability
    this.attachSelectionMonitoring();
    console.log('[PhotoSelection] Initialized - using native browser selection');
  }

  /**
   * Monitor when user finishes selecting photos
   */
  attachSelectionMonitoring() {
    const gridContainer = document.querySelector('.photo-grid');
    if (!gridContainer) {
      console.warn('[PhotoSelection] Grid container not found for monitoring');
      return;
    }

    // When selection ends (mouseup on grid), check what's selected
    gridContainer.addEventListener('mouseup', () => {
      // Small delay to let selection finalize
      setTimeout(() => {
        this.updateSelection();
      }, 50);
    });

    // Also check on selection change events
    document.addEventListener('selectionchange', () => {
      // Debounce - only check if selection hasn't changed for 100ms
      clearTimeout(this.selectionChangeTimeout);
      this.selectionChangeTimeout = setTimeout(() => {
        this.updateSelection();
      }, 100);
    });
  }

  /**
   * Update stored selection and visual feedback
   */
  updateSelection() {
    const selection = window.getSelection();

    // Check if selection is within photo grid
    const gridContainer = document.querySelector('.photo-grid');
    if (!gridContainer || !selection || selection.rangeCount === 0) {
      this.setSelectedPhotoIds([]);
      return;
    }

    // Check if selection is actually in the grid
    const range = selection.getRangeAt(0);
    if (!gridContainer.contains(range.commonAncestorContainer)) {
      this.setSelectedPhotoIds([]);
      return;
    }

    // Get selected photo IDs
    const photoIds = this.getSelectedPhotoIds();
    this.setSelectedPhotoIds(photoIds);
  }

  /**
   * Store selected IDs and update UI feedback
   */
  setSelectedPhotoIds(photoIds) {
    this.selectedPhotoIds = photoIds;

    const sidebar = document.querySelector('.sidebar-left');
    if (!sidebar) return;

    if (photoIds.length > 0) {
      sidebar.classList.add('photos-selected');
      console.log('[PhotoSelection] Selection active:', photoIds.length, 'photos');
    } else {
      sidebar.classList.remove('photos-selected');
      console.log('[PhotoSelection] Selection cleared');
    }
  }

  /**
   * Get photo IDs from current text selection
   * Called by DragDropManager when user drops on a collection
   */
  getSelectedPhotoIds() {
    const selection = window.getSelection();
    console.log('[PhotoSelection] Getting selected photo IDs, selection:', selection);

    if (!selection || selection.rangeCount === 0) {
      console.log('[PhotoSelection] No selection or ranges');
      return [];
    }

    const gridContainer = document.querySelector('.photo-grid');
    if (!gridContainer) {
      console.log('[PhotoSelection] Grid container not found');
      return [];
    }

    // Find all photo cards in the current selection
    const photoCards = Array.from(gridContainer.querySelectorAll('.photo-card'));
    console.log('[PhotoSelection] Found', photoCards.length, 'photo cards');

    const selectedPhotoIds = [];

    photoCards.forEach(card => {
      if (selection.containsNode(card, true)) {
        const photoId = card.dataset.photoId;
        console.log('[PhotoSelection] Card selected, photoId:', photoId);
        if (photoId) {
          selectedPhotoIds.push(photoId);
        }
      }
    });

    console.log('[PhotoSelection] Selected photo IDs:', selectedPhotoIds);
    return selectedPhotoIds;
  }

  /**
   * Clear selection after successful drop
   */
  clearSelection() {
    window.getSelection().removeAllRanges();
    this.setSelectedPhotoIds([]);
  }

  /**
   * Reinitialize - reattach handlers after grid re-render
   */
  reinit() {
    this.attachSelectionMonitoring();
  }
}
