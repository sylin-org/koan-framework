/**
 * PerformanceChart Component
 * Simple line chart for performance metrics using Canvas
 */
export class PerformanceChart {
  constructor(options) {
    this.data = options.data || [];
    this.width = options.width || 600;
    this.height = options.height || 300;
    this.title = options.title || 'Performance';
    this.yAxisLabel = options.yAxisLabel || 'Latency (ms)';
    this.xAxisLabel = options.xAxisLabel || 'Time';
    this.lineColor = options.lineColor || '#3b82f6';
    this.canvasId = options.canvasId || 'perf-chart-' + Math.random().toString(36).substr(2, 9);
  }

  render() {
    return `
      <div class="performance-chart">
        <div class="chart-header">
          <h3 class="chart-title">${this.title}</h3>
        </div>
        <div class="chart-body">
          <canvas id="${this.canvasId}" width="${this.width}" height="${this.height}"></canvas>
        </div>
        <div class="chart-footer">
          <div class="chart-legend">
            <span class="legend-item">
              <span class="legend-color" style="background-color: ${this.lineColor}"></span>
              <span>${this.yAxisLabel}</span>
            </span>
          </div>
        </div>
      </div>
    `;
  }

  renderTo(container) {
    if (typeof container === 'string') {
      container = document.querySelector(container);
    }
    if (container) {
      container.innerHTML = this.render();
      requestAnimationFrame(() => {
        this.drawChart();
      });
    }
  }

  drawChart() {
    const canvas = document.getElementById(this.canvasId);
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    const padding = 40;
    const chartWidth = this.width - 2 * padding;
    const chartHeight = this.height - 2 * padding;

    // Clear canvas
    ctx.clearRect(0, 0, this.width, this.height);

    if (this.data.length === 0) {
      // Show empty state
      ctx.fillStyle = '#9ca3af';
      ctx.font = '14px sans-serif';
      ctx.textAlign = 'center';
      ctx.fillText('No data available', this.width / 2, this.height / 2);
      return;
    }

    // Calculate min/max for scaling
    const values = this.data.map(d => d.value);
    const minValue = Math.min(...values);
    const maxValue = Math.max(...values);
    const valueRange = maxValue - minValue || 1;

    // Draw axes
    ctx.strokeStyle = '#e5e7eb';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(padding, padding);
    ctx.lineTo(padding, this.height - padding);
    ctx.lineTo(this.width - padding, this.height - padding);
    ctx.stroke();

    // Draw grid lines
    ctx.strokeStyle = '#f3f4f6';
    ctx.lineWidth = 1;
    for (let i = 0; i <= 5; i++) {
      const y = padding + (chartHeight * i) / 5;
      ctx.beginPath();
      ctx.moveTo(padding, y);
      ctx.lineTo(this.width - padding, y);
      ctx.stroke();

      // Y-axis labels
      const value = maxValue - (valueRange * i) / 5;
      ctx.fillStyle = '#6b7280';
      ctx.font = '11px sans-serif';
      ctx.textAlign = 'right';
      ctx.fillText(Math.round(value), padding - 5, y + 4);
    }

    // Draw line
    ctx.strokeStyle = this.lineColor;
    ctx.lineWidth = 2;
    ctx.beginPath();

    this.data.forEach((point, index) => {
      const x = padding + (chartWidth * index) / (this.data.length - 1);
      const y = this.height - padding - ((point.value - minValue) / valueRange) * chartHeight;

      if (index === 0) {
        ctx.moveTo(x, y);
      } else {
        ctx.lineTo(x, y);
      }
    });

    ctx.stroke();

    // Draw points
    ctx.fillStyle = this.lineColor;
    this.data.forEach((point, index) => {
      const x = padding + (chartWidth * index) / (this.data.length - 1);
      const y = this.height - padding - ((point.value - minValue) / valueRange) * chartHeight;

      ctx.beginPath();
      ctx.arc(x, y, 3, 0, 2 * Math.PI);
      ctx.fill();
    });
  }

  static createFromTrends(performanceTrends) {
    const data = performanceTrends.dataPoints.map(dp => ({
      label: new Date(dp.timestamp).toLocaleTimeString(),
      value: dp.avgLatencyMs
    }));

    return new PerformanceChart({
      data,
      title: `Performance Trends (${performanceTrends.period})`,
      yAxisLabel: 'Avg Latency (ms)',
      width: 600,
      height: 300
    });
  }
}

// Add styles for PerformanceChart
const style = document.createElement('style');
style.textContent = `
  .performance-chart {
    background-color: var(--color-background-elevated);
    border: 1px solid var(--color-border);
    border-radius: var(--border-radius-lg);
    padding: var(--spacing-4);
  }

  .chart-header {
    margin-bottom: var(--spacing-4);
  }

  .chart-title {
    font-size: var(--font-size-lg);
    font-weight: var(--font-weight-semibold);
    color: var(--color-text-primary);
  }

  .chart-body {
    overflow-x: auto;
  }

  .chart-body canvas {
    max-width: 100%;
    height: auto;
  }

  .chart-footer {
    margin-top: var(--spacing-3);
    padding-top: var(--spacing-3);
    border-top: 1px solid var(--color-border);
  }

  .chart-legend {
    display: flex;
    gap: var(--spacing-4);
    flex-wrap: wrap;
  }

  .legend-item {
    display: flex;
    align-items: center;
    gap: var(--spacing-2);
    font-size: var(--font-size-sm);
    color: var(--color-text-secondary);
  }

  .legend-color {
    display: inline-block;
    width: 1rem;
    height: 0.25rem;
    border-radius: var(--border-radius-sm);
  }
`;
document.head.appendChild(style);
