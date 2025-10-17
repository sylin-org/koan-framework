/**
 * Timeline Component
 * Vertical event timeline view
 */

export class Timeline {
  constructor(app) {
    this.app = app;
    this.container = document.querySelector('.timeline-container');
  }

  render() {
    if (!this.container) return;

    const events = this.app.state.events;

    if (events.length === 0) {
      this.container.innerHTML = `
        <div class="empty-state-hero">
          <svg class="icon" width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect>
            <line x1="16" y1="2" x2="16" y2="6"></line>
            <line x1="8" y1="2" x2="8" y2="6"></line>
            <line x1="3" y1="10" x2="21" y2="10"></line>
          </svg>
          <h3>No events yet</h3>
          <p>Upload photos to see your timeline</p>
        </div>
      `;
      return;
    }

    // Group events by month/year
    const grouped = this.groupByMonthYear(events);

    this.container.innerHTML = `
      <div class="timeline">
        ${Object.entries(grouped).map(([monthYear, events]) => `
          <div class="timeline-group">
            <h3 class="timeline-header">${monthYear}</h3>
            <div class="timeline-events">
              ${events.map(event => this.renderEventCard(event)).join('')}
            </div>
          </div>
        `).join('')}
      </div>
    `;

    // Attach click handlers to event cards
    this.attachEventHandlers();
  }

  attachEventHandlers() {
    const eventCards = this.container.querySelectorAll('.event-card');
    eventCards.forEach(card => {
      card.addEventListener('click', async () => {
        const eventId = card.dataset.eventId;

        // Switch to gallery workspace
        this.app.switchWorkspace('gallery');

        // Filter photos by this event
        await this.app.filterPhotosByEvent(eventId);
      });
    });
  }

  groupByMonthYear(events) {
    const grouped = {};
    events.forEach(event => {
      const date = new Date(event.date || event.createdAt);
      const monthYear = new Intl.DateTimeFormat('en-US', {
        month: 'long',
        year: 'numeric'
      }).format(date);

      if (!grouped[monthYear]) {
        grouped[monthYear] = [];
      }
      grouped[monthYear].push(event);
    });
    return grouped;
  }

  renderEventCard(event) {
    return `
      <article class="event-card" data-event-id="${event.id}">
        <div class="event-date">
          ${this.formatDate(event.date || event.createdAt)}
        </div>
        <div class="event-content">
          <h4 class="event-title">${this.escapeHtml(event.name)}</h4>
          <p class="event-meta">${event.photoCount || 0} photos</p>
        </div>
      </article>
    `;
  }

  formatDate(isoString) {
    const date = new Date(isoString);
    return new Intl.DateTimeFormat('en-US', {
      month: 'short',
      day: 'numeric'
    }).format(date);
  }

  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }
}
