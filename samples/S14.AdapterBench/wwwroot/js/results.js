// Results display and analysis module
const Results = {
    currentResults: null,

    display(results) {
        this.currentResults = results;

        // Show results panel
        document.getElementById('resultsPanel').style.display = 'block';

        // Update overview stats
        this.updateOverviewStats(results);

        // Generate charts
        Charts.createOverviewChart(results);
        Charts.createPerformanceChart('singleWriteChart', results, 'Single Writes');
        Charts.createPerformanceChart('batchWriteChart', results, 'Batch Writes');
        Charts.createPerformanceChart('readByIdChart', results, 'Read By ID');

        // Generate recommendations
        this.generateRecommendations(results);

        // Display detailed results
        this.displayDetailedResults(results);
    },

    updateOverviewStats(results) {
        const duration = (results.completedAt - results.startedAt) / 1000; // milliseconds to seconds
        document.getElementById('totalDuration').textContent = `${duration.toFixed(2)}s`;
        document.getElementById('entitiesTested').textContent = results.entityCount.toLocaleString();
        document.getElementById('providersCount').textContent = results.providerResults.length;

        const totalTests = results.providerResults.reduce((sum, p) => sum + p.tests.length, 0);
        document.getElementById('testsCompleted').textContent = totalTests;
    },

    generateRecommendations(results) {
        const container = document.getElementById('recommendations');
        container.innerHTML = '';

        // Analyze results to generate recommendations
        const writePerformance = this.analyzePerformance(results, 'Single Writes');
        const readPerformance = this.analyzePerformance(results, 'Read By ID');
        const batchPerformance = this.analyzePerformance(results, 'Batch Writes');

        const recommendations = [
            {
                title: 'Best for Write-Heavy Workloads',
                provider: writePerformance.best.provider,
                reason: `Fastest single write performance (${Math.round(writePerformance.best.opsPerSec).toLocaleString()} ops/sec)`
            },
            {
                title: 'Best for Read-Heavy Workloads',
                provider: readPerformance.best.provider,
                reason: `Fastest read by ID performance (${Math.round(readPerformance.best.opsPerSec).toLocaleString()} ops/sec)`
            },
            {
                title: 'Best for Batch Operations',
                provider: batchPerformance.best.provider,
                reason: `Fastest batch write performance (${Math.round(batchPerformance.best.opsPerSec).toLocaleString()} ops/sec)`
            },
            {
                title: 'Most Balanced',
                provider: this.findMostBalanced(results),
                reason: 'Consistent performance across all test types'
            }
        ];

        recommendations.forEach(rec => {
            const card = document.createElement('div');
            card.className = 'recommendation-card';
            card.innerHTML = `
                <h4>${rec.title}</h4>
                <div class="provider-badge" style="background-color: ${Charts.providerColors[rec.provider.toLowerCase()] || '#666'}">
                    ${rec.provider.toUpperCase()}
                </div>
                <p>${rec.reason}</p>
            `;
            container.appendChild(card);
        });
    },

    analyzePerformance(results, testName) {
        let best = { provider: '', opsPerSec: 0 };

        results.providerResults.forEach(provider => {
            const tests = provider.tests.filter(t => t.testName === testName);
            const avgOpsPerSec = tests.reduce((sum, t) => sum + t.operationsPerSecond, 0) / tests.length;

            if (avgOpsPerSec > best.opsPerSec) {
                best = { provider: provider.providerName, opsPerSec: avgOpsPerSec };
            }
        });

        return { best };
    },

    findMostBalanced(results) {
        const scores = {};

        results.providerResults.forEach(provider => {
            const avgOpsPerSec = provider.tests.reduce((sum, t) => sum + t.operationsPerSecond, 0) / provider.tests.length;
            const variance = this.calculateVariance(provider.tests.map(t => t.operationsPerSecond));

            // Lower variance means more balanced (combined with decent performance)
            scores[provider.providerName] = avgOpsPerSec / (variance + 1);
        });

        return Object.keys(scores).reduce((a, b) => scores[a] > scores[b] ? a : b);
    },

    calculateVariance(values) {
        const avg = values.reduce((a, b) => a + b, 0) / values.length;
        const squareDiffs = values.map(v => Math.pow(v - avg, 2));
        return Math.sqrt(squareDiffs.reduce((a, b) => a + b, 0) / values.length);
    },

    displayDetailedResults(results) {
        const container = document.getElementById('detailedResults');
        container.innerHTML = '';

        results.providerResults.forEach(provider => {
            const section = document.createElement('div');
            section.className = 'provider-section';

            let html = `
                <h3>${provider.providerName.toUpperCase()}</h3>
                <p>
                    ${provider.isContainerized ? '🐳 Containerized' : '💾 In-Process'} |
                    Total Duration: ${(provider.totalDuration / 1000000000).toFixed(2)}s
                </p>
                <table class="results-table">
                    <thead>
                        <tr>
                            <th>Test</th>
                            <th>Entity Tier</th>
                            <th>Duration</th>
                            <th>Ops/Sec</th>
                            <th>Execution</th>
                        </tr>
                    </thead>
                    <tbody>
            `;

            provider.tests.forEach(test => {
                html += `
                    <tr>
                        <td>${test.testName}</td>
                        <td>${test.entityTier}</td>
                        <td>${(test.duration / 1000000000).toFixed(2)}s</td>
                        <td>${Math.round(test.operationsPerSecond).toLocaleString()}</td>
                        <td>${test.usedNativeExecution ? '✓ Native' : '⚠ Fallback'}</td>
                    </tr>
                `;
            });

            html += `
                    </tbody>
                </table>
            `;

            section.innerHTML = html;
            container.appendChild(section);
        });
    }
};
