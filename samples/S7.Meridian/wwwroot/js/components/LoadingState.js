/**
 * LoadingState - Professional loading skeletons
 *
 * Design Pattern: Show content structure while loading
 * Features:
 * - Skeleton screens (better than spinners)
 * - Context-specific layouts
 * - Pulsing animation
 * - Prevents layout shift
 */
export class LoadingState {
  /**
   * Render loading skeleton
   * @param {string} variant - Type of skeleton to show
   * @param {Object} options - Additional options
   * @returns {string} HTML string
   */
  static render(variant = 'default', options = {}) {
    const {
      count = 3,
      compact = false
    } = options;

    const variants = {
      list: LoadingState.renderList(count, compact),
      card: LoadingState.renderCard(count),
      table: LoadingState.renderTable(count),
      form: LoadingState.renderForm(),
      page: LoadingState.renderPage(),
      detail: LoadingState.renderDetail()
    };

    return variants[variant] || LoadingState.renderSpinner();
  }

  /**
   * List skeleton (for entity lists)
   */
  static renderList(count = 3, compact = false) {
    const items = Array(count).fill(0).map((_, i) => `
      <div class="skeleton-list-item ${compact ? 'compact' : ''}" key="${i}">
        <div class="skeleton-list-icon skeleton-pulse"></div>
        <div class="skeleton-list-content">
          <div class="skeleton-line skeleton-pulse" style="width: 60%;"></div>
          <div class="skeleton-line skeleton-pulse" style="width: 80%;"></div>
        </div>
        <div class="skeleton-list-actions">
          <div class="skeleton-button skeleton-pulse"></div>
          <div class="skeleton-button skeleton-pulse"></div>
        </div>
      </div>
    `).join('');

    return `
      <div class="loading-skeleton" data-loading-skeleton>
        <div class="skeleton-list">
          ${items}
        </div>
      </div>
    `;
  }

  /**
   * Card grid skeleton
   */
  static renderCard(count = 3) {
    const cards = Array(count).fill(0).map((_, i) => `
      <div class="skeleton-card" key="${i}">
        <div class="skeleton-card-header">
          <div class="skeleton-line skeleton-pulse" style="width: 70%;"></div>
          <div class="skeleton-line skeleton-pulse" style="width: 50%;"></div>
        </div>
        <div class="skeleton-card-body">
          <div class="skeleton-line skeleton-pulse" style="width: 100%;"></div>
          <div class="skeleton-line skeleton-pulse" style="width: 90%;"></div>
          <div class="skeleton-line skeleton-pulse" style="width: 80%;"></div>
        </div>
        <div class="skeleton-card-footer">
          <div class="skeleton-button skeleton-pulse"></div>
          <div class="skeleton-button skeleton-pulse"></div>
        </div>
      </div>
    `).join('');

    return `
      <div class="loading-skeleton" data-loading-skeleton>
        <div class="skeleton-card-grid">
          ${cards}
        </div>
      </div>
    `;
  }

  /**
   * Table skeleton
   */
  static renderTable(count = 5) {
    const rows = Array(count).fill(0).map((_, i) => `
      <tr class="skeleton-table-row" key="${i}">
        <td><div class="skeleton-line skeleton-pulse" style="width: 80%;"></div></td>
        <td><div class="skeleton-line skeleton-pulse" style="width: 60%;"></div></td>
        <td><div class="skeleton-line skeleton-pulse" style="width: 70%;"></div></td>
        <td><div class="skeleton-button skeleton-pulse"></div></td>
      </tr>
    `).join('');

    return `
      <div class="loading-skeleton" data-loading-skeleton>
        <div class="skeleton-table">
          <div class="skeleton-table-header">
            <div class="skeleton-line skeleton-pulse" style="width: 100px;"></div>
            <div class="skeleton-line skeleton-pulse" style="width: 100px;"></div>
            <div class="skeleton-line skeleton-pulse" style="width: 100px;"></div>
            <div class="skeleton-line skeleton-pulse" style="width: 100px;"></div>
          </div>
          <table>
            <tbody>
              ${rows}
            </tbody>
          </table>
        </div>
      </div>
    `;
  }

  /**
   * Form skeleton
   */
  static renderForm() {
    return `
      <div class="loading-skeleton" data-loading-skeleton>
        <div class="skeleton-form">
          <div class="skeleton-form-field">
            <div class="skeleton-line skeleton-pulse" style="width: 100px;"></div>
            <div class="skeleton-input skeleton-pulse"></div>
          </div>
          <div class="skeleton-form-field">
            <div class="skeleton-line skeleton-pulse" style="width: 120px;"></div>
            <div class="skeleton-input skeleton-pulse"></div>
          </div>
          <div class="skeleton-form-field">
            <div class="skeleton-line skeleton-pulse" style="width: 90px;"></div>
            <div class="skeleton-textarea skeleton-pulse"></div>
          </div>
          <div class="skeleton-form-actions">
            <div class="skeleton-button skeleton-pulse"></div>
            <div class="skeleton-button skeleton-pulse"></div>
          </div>
        </div>
      </div>
    `;
  }

  /**
   * Full page skeleton
   */
  static renderPage() {
    return `
      <div class="loading-skeleton loading-skeleton-page" data-loading-skeleton>
        <div class="skeleton-page-header">
          <div class="skeleton-line skeleton-pulse" style="width: 200px; height: 32px;"></div>
          <div class="skeleton-button skeleton-pulse"></div>
        </div>
        <div class="skeleton-page-content">
          ${LoadingState.renderCard(4)}
        </div>
      </div>
    `;
  }

  /**
   * Detail view skeleton
   */
  static renderDetail() {
    return `
      <div class="loading-skeleton" data-loading-skeleton>
        <div class="skeleton-detail">
          <div class="skeleton-detail-header">
            <div class="skeleton-line skeleton-pulse" style="width: 250px; height: 28px;"></div>
            <div class="skeleton-line skeleton-pulse" style="width: 150px;"></div>
          </div>
          <div class="skeleton-detail-section">
            <div class="skeleton-line skeleton-pulse" style="width: 150px;"></div>
            <div class="skeleton-detail-fields">
              <div class="skeleton-detail-field">
                <div class="skeleton-line skeleton-pulse" style="width: 80px;"></div>
                <div class="skeleton-line skeleton-pulse" style="width: 200px;"></div>
              </div>
              <div class="skeleton-detail-field">
                <div class="skeleton-line skeleton-pulse" style="width: 100px;"></div>
                <div class="skeleton-line skeleton-pulse" style="width: 250px;"></div>
              </div>
              <div class="skeleton-detail-field">
                <div class="skeleton-line skeleton-pulse" style="width: 70px;"></div>
                <div class="skeleton-line skeleton-pulse" style="width: 180px;"></div>
              </div>
            </div>
          </div>
          <div class="skeleton-detail-section">
            <div class="skeleton-line skeleton-pulse" style="width: 180px;"></div>
            <div class="skeleton-detail-fields">
              <div class="skeleton-detail-field">
                <div class="skeleton-line skeleton-pulse" style="width: 90px;"></div>
                <div class="skeleton-line skeleton-pulse" style="width: 220px;"></div>
              </div>
              <div class="skeleton-detail-field">
                <div class="skeleton-line skeleton-pulse" style="width: 110px;"></div>
                <div class="skeleton-line skeleton-pulse" style="width: 190px;"></div>
              </div>
            </div>
          </div>
        </div>
      </div>
    `;
  }

  /**
   * Fallback spinner (use sparingly)
   */
  static renderSpinner() {
    return `
      <div class="loading-spinner" data-loading-spinner>
        <div class="spinner">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83"></path>
          </svg>
        </div>
        <p class="loading-text">Loading...</p>
      </div>
    `;
  }

  /**
   * Inline loader (for buttons, etc.)
   */
  static renderInline() {
    return `
      <span class="loading-inline" data-loading-inline>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83"></path>
        </svg>
      </span>
    `;
  }
}
