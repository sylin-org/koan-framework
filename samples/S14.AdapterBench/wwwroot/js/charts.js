// Chart.js visualization module
const Charts = {
    charts: {},

    providerColors: {
        'sqlite': '#4ade80',
        'postgres': '#3b82f6',
        'mongo': '#10b981',
        'redis': '#ef4444'
    },

    createOverviewChart(results) {
        const ctx = document.getElementById('overviewChart').getContext('2d');

        const data = {
            labels: results.providerResults.map(p => p.providerName.toUpperCase()),
            datasets: [{
                label: 'Total Duration (seconds)',
                data: results.providerResults.map(p => p.totalDuration / 1000000000), // nanoseconds to seconds
                backgroundColor: results.providerResults.map(p =>
                    this.providerColors[p.providerName.toLowerCase()] || '#666'),
            }]
        };

        if (this.charts.overview) {
            this.charts.overview.destroy();
        }

        this.charts.overview = new Chart(ctx, {
            type: 'bar',
            data: data,
            options: {
                responsive: true,
                maintainAspectRatio: true,
                indexAxis: 'y',
                plugins: {
                    legend: {
                        display: false
                    },
                    title: {
                        display: true,
                        text: 'Lower is better'
                    }
                },
                scales: {
                    x: {
                        beginAtZero: true,
                        title: {
                            display: true,
                            text: 'Duration (seconds)'
                        }
                    }
                }
            }
        });
    },

    createPerformanceChart(canvasId, results, testName) {
        const ctx = document.getElementById(canvasId).getContext('2d');

        const datasets = results.providerResults.map(provider => {
            const testData = provider.tests
                .filter(t => t.testName === testName)
                .map(t => ({
                    x: t.entityTier,
                    y: t.operationsPerSecond
                }));

            return {
                label: provider.providerName.toUpperCase(),
                data: testData,
                backgroundColor: this.providerColors[provider.providerName.toLowerCase()] || '#666',
            };
        });

        if (this.charts[canvasId]) {
            this.charts[canvasId].destroy();
        }

        this.charts[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: ['Minimal', 'Indexed', 'Complex'],
                datasets: datasets
            },
            options: {
                responsive: true,
                maintainAspectRatio: true,
                plugins: {
                    legend: {
                        position: 'top'
                    },
                    title: {
                        display: true,
                        text: 'Higher is better'
                    }
                },
                scales: {
                    y: {
                        beginAtZero: true,
                        title: {
                            display: true,
                            text: 'Operations per Second'
                        }
                    }
                }
            }
        });
    }
};
