// Main application logic
const App = {
    selectedProviders: [],
    selectedTiers: [],
    currentMode: 'Sequential',
    currentJobId: null,
    pollInterval: null,

    async init() {
        await this.loadProviders();
        await this.loadTiers();
        this.setupEventListeners();
        await Progress.connect();
        Progress.onProgressUpdate = (progress) => Progress.updateUI(progress);
    },

    async loadProviders() {
        const providers = await API.getProviders();
        const container = document.getElementById('providerCheckboxes');

        providers.forEach(provider => {
            const label = document.createElement('label');
            label.className = 'checkbox-label';

            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.value = provider.name;
            checkbox.checked = provider.isDefault;

            if (provider.isDefault) {
                this.selectedProviders.push(provider.name);
            }

            checkbox.addEventListener('change', (e) => {
                if (e.target.checked) {
                    this.selectedProviders.push(provider.name);
                } else {
                    this.selectedProviders = this.selectedProviders.filter(p => p !== provider.name);
                }
            });

            const text = document.createTextNode(
                ` ${provider.displayName} ${provider.isContainerized ? '(🐳 Container)' : '(💾 In-Process)'}`
            );

            label.appendChild(checkbox);
            label.appendChild(text);
            container.appendChild(label);
        });
    },

    async loadTiers() {
        const tiers = await API.getTiers();
        const container = document.getElementById('tierCheckboxes');

        tiers.forEach(tier => {
            const label = document.createElement('label');
            label.className = 'checkbox-label';

            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.value = tier.name;
            checkbox.checked = tier.isDefault;

            if (tier.isDefault) {
                this.selectedTiers.push(tier.name);
            }

            checkbox.addEventListener('change', (e) => {
                if (e.target.checked) {
                    this.selectedTiers.push(tier.name);
                } else {
                    this.selectedTiers = this.selectedTiers.filter(t => t !== tier.name);
                }
            });

            const text = document.createTextNode(` ${tier.name} - ${tier.description}`);

            label.appendChild(checkbox);
            label.appendChild(text);
            container.appendChild(label);
        });
    },

    setupEventListeners() {
        // Mode toggle
        document.getElementById('sequentialBtn').addEventListener('click', () => {
            this.setMode('Sequential');
        });

        document.getElementById('parallelBtn').addEventListener('click', () => {
            this.setMode('Parallel');
        });

        // Run benchmark
        document.getElementById('runBenchmarkBtn').addEventListener('click', async () => {
            await this.runBenchmark();
        });

        // Stop benchmark
        document.getElementById('stopBenchmarkBtn').addEventListener('click', async () => {
            await this.stopBenchmark();
        });

        // Tab switching
        document.querySelectorAll('.tab-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                this.switchTab(e.target.dataset.tab);
            });
        });
    },

    setMode(mode) {
        this.currentMode = mode;

        const sequentialBtn = document.getElementById('sequentialBtn');
        const parallelBtn = document.getElementById('parallelBtn');

        if (mode === 'Sequential') {
            sequentialBtn.classList.add('active');
            parallelBtn.classList.remove('active');
        } else {
            parallelBtn.classList.add('active');
            sequentialBtn.classList.remove('active');
        }
    },

    async runBenchmark() {
        // Validate selection
        if (this.selectedProviders.length === 0) {
            alert('Please select at least one provider');
            return;
        }

        if (this.selectedTiers.length === 0) {
            alert('Please select at least one entity tier');
            return;
        }

        // Hide results, show progress
        document.getElementById('resultsPanel').style.display = 'none';
        document.getElementById('progressPanel').style.display = 'block';
        document.getElementById('runBenchmarkBtn').style.display = 'none';
        document.getElementById('stopBenchmarkBtn').style.display = 'inline-block';

        // Clear previous progress
        document.getElementById('testLog').innerHTML = '';
        document.getElementById('progressBar').style.width = '0%';
        document.getElementById('currentMode').textContent = this.currentMode;
        Progress.clearProviderProgress();

        // Build request
        const request = {
            mode: this.currentMode === 'Sequential' ? 0 : 1,
            scale: document.getElementById('scaleSelect').selectedIndex,
            providers: this.selectedProviders,
            entityTiers: this.selectedTiers
        };

        try {
            // Run benchmark synchronously with real-time SignalR progress
            const result = await API.runBenchmark(request);

            // Display results immediately
            if (result) {
                // Convert TimeSpan strings to nanoseconds for display
                result.providerResults.forEach(p => {
                    p.totalDuration = this.parseTimeSpan(p.totalDuration);
                    p.tests.forEach(t => {
                        t.duration = this.parseTimeSpan(t.duration);
                    });
                });

                result.startedAt = new Date(result.startedAt);
                result.completedAt = new Date(result.completedAt);

                Results.display(result);
            }

            this.resetUI();
        } catch (error) {
            console.error('Benchmark failed:', error);
            alert(`Benchmark failed: ${error.message}`);

            this.resetUI();
        }
    },

    async pollJobStatus() {
        if (!this.currentJobId) return;

        this.pollInterval = setInterval(async () => {
            try {
                const status = await API.getJobStatus(this.currentJobId);

                // Update progress UI
                this.updateProgress(status);

                // Check if job is complete
                if (status.status === 'Completed') {
                    clearInterval(this.pollInterval);
                    this.pollInterval = null;

                    // Display results
                    if (status.result) {
                        // Convert TimeSpan strings to nanoseconds for display
                        status.result.providerResults.forEach(p => {
                            p.totalDuration = this.parseTimeSpan(p.totalDuration);
                            p.tests.forEach(t => {
                                t.duration = this.parseTimeSpan(t.duration);
                            });
                        });

                        status.result.startedAt = new Date(status.result.startedAt);
                        status.result.completedAt = new Date(status.result.completedAt);

                        Results.display(status.result);
                    }

                    this.resetUI();
                } else if (status.status === 'Failed' || status.status === 'Cancelled') {
                    clearInterval(this.pollInterval);
                    this.pollInterval = null;

                    alert(`Benchmark ${status.status.toLowerCase()}: ${status.error || 'Unknown error'}`);
                    this.resetUI();
                }

            } catch (error) {
                console.error('Failed to poll job status:', error);
                clearInterval(this.pollInterval);
                this.pollInterval = null;
                alert(`Failed to get job status: ${error.message}`);
                this.resetUI();
            }
        }, 1000); // Poll every second
    },

    updateProgress(status) {
        // Update progress bar
        const progressBar = document.getElementById('progressBar');
        progressBar.style.width = `${status.progress * 100}%`;

        // Update progress message
        if (status.progressMessage) {
            const testLog = document.getElementById('testLog');
            const logEntry = document.createElement('div');
            logEntry.className = 'log-entry';
            logEntry.textContent = `[${new Date().toLocaleTimeString()}] ${status.progressMessage}`;
            testLog.appendChild(logEntry);
            testLog.scrollTop = testLog.scrollHeight;
        }
    },

    async stopBenchmark() {
        if (!this.currentJobId) return;

        try {
            await API.cancelJob(this.currentJobId);

            // Stop polling
            if (this.pollInterval) {
                clearInterval(this.pollInterval);
                this.pollInterval = null;
            }

            alert('Benchmark cancelled');
            this.resetUI();

        } catch (error) {
            console.error('Failed to cancel benchmark:', error);
            alert(`Failed to cancel benchmark: ${error.message}`);
        }
    },

    resetUI() {
        document.getElementById('progressPanel').style.display = 'none';
        document.getElementById('runBenchmarkBtn').style.display = 'inline-block';
        document.getElementById('stopBenchmarkBtn').style.display = 'none';
        Progress.clearProviderProgress();
        this.currentJobId = null;
    },

    parseTimeSpan(timeSpan) {
        // C# TimeSpan format: "hh:mm:ss.fffffff"
        // We need to convert to total nanoseconds for consistency
        if (typeof timeSpan === 'string') {
            const parts = timeSpan.split(':');
            const hours = parseInt(parts[0] || 0);
            const minutes = parseInt(parts[1] || 0);
            const seconds = parseFloat(parts[2] || 0);

            return (hours * 3600 + minutes * 60 + seconds) * 1000000000; // nanoseconds
        }
        return timeSpan;
    },

    switchTab(tabName) {
        // Update tab buttons
        document.querySelectorAll('.tab-btn').forEach(btn => {
            btn.classList.remove('active');
        });
        document.querySelector(`[data-tab="${tabName}"]`).classList.add('active');

        // Update tab panes
        document.querySelectorAll('.tab-pane').forEach(pane => {
            pane.classList.remove('active');
        });
        document.getElementById(`${tabName}Tab`).classList.add('active');
    }
};

// Initialize app when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    App.init().catch(console.error);
});
