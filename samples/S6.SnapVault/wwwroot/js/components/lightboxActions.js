/**
 * Lightbox Actions Manager
 * Handles all photo actions: favorite, rating, download, delete, AI regeneration
 */

export class LightboxActions {
  constructor(lightbox, app) {
    this.lightbox = lightbox;
    this.app = app;
    this.currentPhoto = null;
  }

  setPhoto(photo) {
    this.currentPhoto = photo;
  }

  async toggleFavorite() {
    if (!this.currentPhoto) return;

    try {
      const response = await this.app.api.post(`/api/photos/${this.currentPhoto.id}/favorite`);

      // Update local state
      this.currentPhoto.isFavorite = response.isFavorite;

      // Update photo in app.photos array
      const photoInList = this.app.photos.find(p => p.id === this.currentPhoto.id);
      if (photoInList) {
        photoInList.isFavorite = response.isFavorite;
      }

      // Update UI in panel
      this.updateFavoriteButton(response.isFavorite);

      // Show toast
      this.app.components.toast.show(
        response.isFavorite ? 'Added to favorites' : 'Removed from favorites',
        { icon: response.isFavorite ? '❤️' : '🤍', duration: 1500 }
      );
    } catch (error) {
      console.error('Failed to toggle favorite:', error);
      this.app.components.toast.show('Failed to update favorite', {
        icon: '⚠️',
        duration: 2000
      });
    }
  }

  updateFavoriteButton(isFavorite) {
    const btn = this.lightbox.panel?.container.querySelector('#btn-favorite');
    if (!btn) return;

    btn.classList.toggle('active', isFavorite);
    const svg = btn.querySelector('svg');
    if (svg) {
      svg.setAttribute('fill', isFavorite ? 'currentColor' : 'none');
    }

    // Heart beat animation
    if (isFavorite) {
      btn.classList.add('animate-heartbeat');
      setTimeout(() => btn.classList.remove('animate-heartbeat'), 600);
    }
  }

  async setRating(rating) {
    if (!this.currentPhoto || rating < 0 || rating > 5) return;

    try {
      const response = await this.app.api.post(`/api/photos/${this.currentPhoto.id}/rate`, { rating });

      // Update local state
      this.currentPhoto.rating = response.rating;

      // Update photo in app.photos array
      const photoInList = this.app.photos.find(p => p.id === this.currentPhoto.id);
      if (photoInList) {
        photoInList.rating = response.rating;
      }

      // Update UI in panel
      this.updateRatingStars(response.rating);

      // Show toast
      this.app.components.toast.show(
        rating === 0 ? 'Rating removed' : `Rated ${rating} star${rating !== 1 ? 's' : ''}`,
        { icon: '⭐', duration: 1500 }
      );
    } catch (error) {
      console.error('Failed to set rating:', error);
      this.app.components.toast.show('Failed to update rating', {
        icon: '⚠️',
        duration: 2000
      });
    }
  }

  updateRatingStars(rating) {
    const stars = this.lightbox.panel?.container.querySelectorAll('.star-btn');
    if (!stars) return;

    stars.forEach((star, index) => {
      const starRating = index + 1;
      const svg = star.querySelector('svg');
      if (starRating <= rating) {
        star.classList.add('active');
        if (svg) svg.setAttribute('fill', 'currentColor');
      } else {
        star.classList.remove('active');
        if (svg) svg.setAttribute('fill', 'none');
      }
    });
  }

  async download() {
    if (!this.currentPhoto) return;

    try {
      // Open download endpoint in new window
      window.open(`/api/photos/${this.currentPhoto.id}/download`, '_blank');

      this.app.components.toast.show('Download started', {
        icon: '⬇️',
        duration: 2000
      });
    } catch (error) {
      console.error('Failed to download photo:', error);
      this.app.components.toast.show('Failed to download photo', {
        icon: '⚠️',
        duration: 2000
      });
    }
  }

  async deletePhoto() {
    if (!this.currentPhoto) return;

    // Confirmation dialog
    const confirmed = confirm(
      `Delete "${this.currentPhoto.originalFileName}"?\n\nThis action cannot be undone.`
    );

    if (!confirmed) return;

    try {
      const response = await this.app.api.post('/api/photos/bulk/delete', {
        photoIds: [this.currentPhoto.id]
      });

      if (response.deleted > 0) {
        // Success: Close lightbox
        this.lightbox.close();

        // Reload current view (preserves All Photos / Favorites / Collection context)
        this.app.components.collectionView.loadPhotos();

        this.app.components.toast.show('Photo deleted', {
          icon: '✅',
          duration: 3000
        });
      } else {
        throw new Error(response.errors?.[0] || 'Delete failed');
      }
    } catch (error) {
      console.error('Failed to delete photo:', error);
      this.app.components.toast.show('Failed to delete photo', {
        icon: '⚠️',
        duration: 3000
      });
    }
  }

  async regenerateAI() {
    if (!this.currentPhoto) return;

    // Find any regenerate button (supports multiple IDs for different states)
    const btn = this.lightbox.panel?.container.querySelector('.btn-regenerate-ai');

    let originalContent = null;
    if (btn) {
      // Show loading state
      originalContent = btn.innerHTML;
      btn.disabled = true;
      btn.style.opacity = '0.5';
      btn.innerHTML = `
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="animation: spin 1s linear infinite;">
          <polyline points="23 4 23 10 17 10"></polyline>
          <polyline points="1 20 1 14 7 14"></polyline>
          <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
        </svg>
        Regenerating...
      `;
    }

    try {
      // Call API to regenerate AI metadata
      await this.app.api.post(`/api/photos/${this.currentPhoto.id}/regenerate-ai`);

      this.app.components.toast.show('AI description regeneration started', {
        icon: '🔄',
        duration: 3000
      });

      // Poll for completion (max 60s)
      const startTime = Date.now();
      const pollInterval = 1000;
      const timeout = 60000;

      const poll = async () => {
        if (Date.now() - startTime > timeout) {
          throw new Error('Regeneration timed out');
        }

        // Fetch updated photo data
        const updatedPhoto = await this.app.api.get(`/api/photos/${this.currentPhoto.id}`);

        // Check if AI analysis is updated (new structured format or legacy format)
        const hasNewAnalysis = updatedPhoto.aiAnalysis &&
                               JSON.stringify(updatedPhoto.aiAnalysis) !== JSON.stringify(this.currentPhoto.aiAnalysis);
        const hasUpdatedDescription = updatedPhoto.detailedDescription &&
                                      updatedPhoto.detailedDescription !== this.currentPhoto.detailedDescription;

        if (hasNewAnalysis || hasUpdatedDescription) {
          // Success: Update current photo
          this.currentPhoto = updatedPhoto;

          // Update panel display
          if (this.lightbox.panel) {
            this.lightbox.panel.renderAIInsights(updatedPhoto);
          }

          // Restore button (if it exists)
          if (btn && originalContent) {
            btn.disabled = false;
            btn.style.opacity = '1';
            btn.innerHTML = originalContent;
          }

          this.app.components.toast.show('AI description regenerated successfully', {
            icon: '✅',
            duration: 3000
          });
        } else {
          // Still processing, poll again
          setTimeout(poll, pollInterval);
        }
      };

      poll();
    } catch (error) {
      console.error('Failed to regenerate AI:', error);

      // Restore button (if it exists)
      if (btn && originalContent) {
        btn.disabled = false;
        btn.style.opacity = '1';
        btn.innerHTML = originalContent;
      }

      this.app.components.toast.show('Failed to regenerate AI description', {
        icon: '⚠️',
        duration: 5000
      });
    }
  }
}
