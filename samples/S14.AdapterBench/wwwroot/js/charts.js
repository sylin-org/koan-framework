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

        // Collect all unique entity tiers from actual test results
        const tierSet = new Set();
        results.providerResults.forEach(provider => {
            provider.tests
                .filter(t => t.testName === testName)
                .forEach(t => tierSet.add(t.entityTier));
        });
        const tierLabels = Array.from(tierSet).sort((a, b) => {
            const order = { 'Minimal': 1, 'Indexed': 2, 'Complex': 3 };
            return (order[a] || 99) - (order[b] || 99);
        });

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
                labels: tierLabels,
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
    },

    createMigrationChart(results) {
        const ctx = document.getElementById('migrationChart').getContext('2d');

        // Collect all migration test results
        const migrationData = [];
        results.providerResults.forEach(provider => {
            provider.tests
                .filter(t => t.testName.startsWith('Migration ('))
                .forEach(test => {
                    // Extract source and dest from test name like "Migration (sqlite → postgres)"
                    const match = test.testName.match(/Migration \((.+) → (.+)\)/);
                    if (match) {
                        migrationData.push({
                            source: match[1],
                            dest: match[2],
                            tier: test.entityTier,
                            opsPerSec: test.operationsPerSecond,
                            label: `${match[1]} → ${match[2]} (${test.entityTier})`
                        });
                    }
                });
        });

        if (migrationData.length === 0) {
            // No migration data, show message
            ctx.font = '16px Arial';
            ctx.fillText('No migration data available', 10, 30);
            return;
        }

        // Sort by ops/sec descending
        migrationData.sort((a, b) => b.opsPerSec - a.opsPerSec);

        const data = {
            labels: migrationData.map(m => m.label),
            datasets: [{
                label: 'Migration Speed (ops/sec)',
                data: migrationData.map(m => m.opsPerSec),
                backgroundColor: migrationData.map(m =>
                    this.providerColors[m.source.toLowerCase()] || '#666'),
            }]
        };

        if (this.charts.migration) {
            this.charts.migration.destroy();
        }

        this.charts.migration = new Chart(ctx, {
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
                        text: 'Higher is better (operations per second)'
                    }
                },
                scales: {
                    x: {
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
