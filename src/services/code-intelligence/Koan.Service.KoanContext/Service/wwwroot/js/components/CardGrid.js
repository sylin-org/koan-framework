/**
 * CardGrid Component
 * Responsive grid layout for cards
 */
export class CardGrid {
  constructor(options = {}) {
    this.columns = options.columns || 'auto'; // 'auto', 1, 2, 3, 4
    this.gap = options.gap || '4'; // spacing units
    this.items = options.items || [];
  }

  render() {
    const gridClass = this.getGridClass();
    const gapClass = `gap-${this.gap}`;

    const itemsHtml = this.items.map(item => {
      if (typeof item === 'string') {
        return item;
      } else if (item.render) {
        return item.render();
      } else {
        return '';
      }
    }).join('');

    return `
      <div class="card-grid ${gridClass} ${gapClass}">
        ${itemsHtml}
      </div>
    `;
  }

  getGridClass() {
    if (this.columns === 'auto') {
      return 'card-grid-auto';
    }
    return `grid-cols-${this.columns}`;
  }

  static renderTo(container, options) {
    const grid = new CardGrid(options);
    if (typeof container === 'string') {
      container = document.querySelector(container);
    }
    if (container) {
      container.innerHTML = grid.render();
    }
  }
}

// Add styles for CardGrid
const style = document.createElement('style');
style.textContent = `
  .card-grid {
    display: grid;
    width: 100%;
  }

  .card-grid-auto {
    grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
  }

  @media (max-width: 640px) {
    .card-grid-auto {
      grid-template-columns: 1fr;
    }
  }

  @media (min-width: 1280px) {
    .card-grid-auto {
      grid-template-columns: repeat(auto-fill, minmax(350px, 1fr));
    }
  }
`;
document.head.appendChild(style);
