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
      this.clearVisualFeedback();
      this.setSelectedPhotoIds([]);
      return;
    }

    // Check if selection is actually in the grid
    const range = selection.getRangeAt(0);
    if (!gridContainer.contains(range.commonAncestorContainer)) {
      this.clearVisualFeedback();
      this.setSelectedPhotoIds([]);
      return;
    }

    // Get selected photo IDs from text selection
    const photoCards = Array.from(gridContainer.querySelectorAll('.photo-card'));
    const selectedPhotoIds = [];

    photoCards.forEach(card => {
      if (selection.containsNode(card, true)) {
        const photoId = card.dataset.photoId;
        if (photoId) {
          selectedPhotoIds.push(photoId);
        }
      }
    });

    // Update visual feedback
    this.updateVisualFeedback(selectedPhotoIds, photoCards);
    this.setSelectedPhotoIds(selectedPhotoIds);
  }

  /**
   * Update visual feedback on photo cards (blue overlay)
   */
  updateVisualFeedback(selectedPhotoIds, photoCards) {
    const selectedSet = new Set(selectedPhotoIds);

    photoCards.forEach(card => {
      const photoId = card.dataset.photoId;
      const indicator = card.querySelector('.selection-indicator');

      if (selectedSet.has(photoId)) {
        // Add visual feedback
        card.classList.add('selected');
        if (indicator) {
          indicator.style.display = 'flex';
        }
      } else {
        // Remove visual feedback
        card.classList.remove('selected');
        if (indicator) {
          indicator.style.display = 'none';
        }
      }
    });
  }

  /**
   * Clear visual feedback from all photo cards
   */
  clearVisualFeedback() {
    const gridContainer = document.querySelector('.photo-grid');
    if (!gridContainer) return;

    const photoCards = gridContainer.querySelectorAll('.photo-card');
    photoCards.forEach(card => {
      card.classList.remove('selected');
      const indicator = card.querySelector('.selection-indicator');
      if (indicator) {
        indicator.style.display = 'none';
      }
    });
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
   * Get photo IDs from current selection (text selection OR clicked selections OR stored brush selection)
   * Called by DragDropManager when user drops on a collection
   */
  getSelectedPhotoIds() {
    console.log('[PhotoSelection] getSelectedPhotoIds called');

    // FIRST: Check for active text selection (brush selection in progress)
    const selection = window.getSelection();
    const gridContainer = document.querySelector('.photo-grid');

    if (selection && selection.rangeCount > 0 && gridContainer) {
      // Find all photo cards in the current text selection
      const photoCards = Array.from(gridContainer.querySelectorAll('.photo-card'));
      const textSelectedIds = [];

      photoCards.forEach(card => {
        if (selection.containsNode(card, true)) {
          const photoId = card.dataset.photoId;
          if (photoId) {
            textSelectedIds.push(photoId);
          }
        }
      });

      // If we found photos in text selection, return those
      if (textSelectedIds.length > 0) {
        console.log('[PhotoSelection] Using active text selection:', textSelectedIds);
        return textSelectedIds;
      }
    }

    // SECOND: Check stored brush selection (text selection was made, but may have been cleared during drag)
    if (this.selectedPhotoIds && this.selectedPhotoIds.length > 0) {
      console.log('[PhotoSelection] Using stored brush selection:', this.selectedPhotoIds);
      return this.selectedPhotoIds;
    }

    // THIRD: Fall back to clicked selections (single photo drag or multi-select via click)
    if (this.app.state.selectedPhotos && this.app.state.selectedPhotos.size > 0) {
      const clickedIds = Array.from(this.app.state.selectedPhotos);
      console.log('[PhotoSelection] Using clicked selection:', clickedIds);
      return clickedIds;
    }

    console.log('[PhotoSelection] No selection found');
    return [];
  }

  /**
   * Clear selection after successful drop
   */
  clearSelection() {
    // Clear text selection (brush selection)
    window.getSelection().removeAllRanges();

    // Clear visual feedback
    this.clearVisualFeedback();
    this.setSelectedPhotoIds([]);

    // Also clear clicked selections (single photo drag or multi-select)
    if (this.app.clearSelection) {
      this.app.clearSelection();
    }
  }

  /**
   * Reinitialize - reattach handlers after grid re-render
   */
  reinit() {
    this.attachSelectionMonitoring();
  }
}
