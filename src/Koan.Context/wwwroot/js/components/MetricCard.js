/**
 * MetricCard Component
 * Displays a single metric with title, value, and trend indicator
 */
export class MetricCard {
  constructor(options) {
    this.title = options.title;
    this.value = options.value;
    this.change = options.change;
    this.trend = options.trend; // 'up', 'down', 'stable'
    this.icon = options.icon;
    this.variant = options.variant || 'default'; // 'default', 'success', 'warning', 'danger'
  }

  render() {
    const trendIcon = this.getTrendIcon();
    const trendClass = this.getTrendClass();
    const variantClass = this.getVariantClass();

    return `
      <div class="card metric-card ${variantClass}">
        <div class="metric-card-header">
          ${this.icon ? `<span class="metric-icon">${this.icon}</span>` : ''}
          <h3 class="text-small text-secondary mb-0">${this.title}</h3>
        </div>
        <div class="metric-card-body">
          <div class="metric-value text-h2 mb-0">${this.value}</div>
          ${this.change !== undefined ? `
            <div class="metric-change ${trendClass}">
              <span class="metric-trend-icon">${trendIcon}</span>
              <span class="text-small">${this.change}</span>
            </div>
          ` : ''}
        </div>
      </div>
    `;
  }

  getTrendIcon() {
    switch (this.trend) {
      case 'up':
        return '↑';
      case 'down':
        return '↓';
      case 'stable':
        return '→';
      default:
        return '';
    }
  }

  getTrendClass() {
    switch (this.trend) {
      case 'up':
        return 'trend-up text-success';
      case 'down':
        return 'trend-down text-danger';
      case 'stable':
        return 'trend-stable text-secondary';
      default:
        return '';
    }
  }

  getVariantClass() {
    switch (this.variant) {
      case 'success':
        return 'metric-card-success';
      case 'warning':
        return 'metric-card-warning';
      case 'danger':
        return 'metric-card-danger';
      default:
        return '';
    }
  }
}

// Add styles for MetricCard
const style = document.createElement('style');
style.textContent = `
  .metric-card {
    transition: transform var(--transition-fast);
  }

  .metric-card:hover {
    transform: translateY(-2px);
  }

  .metric-card-header {
    display: flex;
    align-items: center;
    gap: var(--spacing-2);
    margin-bottom: var(--spacing-3);
  }

  .metric-icon {
    font-size: var(--font-size-xl);
  }

  .metric-card-body {
    display: flex;
    flex-direction: column;
    gap: var(--spacing-2);
  }

  .metric-value {
    font-weight: var(--font-weight-bold);
  }

  .metric-change {
    display: flex;
    align-items: center;
    gap: var(--spacing-1);
  }

  .metric-trend-icon {
    font-size: var(--font-size-lg);
    font-weight: var(--font-weight-bold);
  }

  .metric-card-success {
    border-left: 4px solid var(--color-success-500);
  }

  .metric-card-warning {
    border-left: 4px solid var(--color-warning-500);
  }

  .metric-card-danger {
    border-left: 4px solid var(--color-danger-500);
  }
`;
document.head.appendChild(style);
