// SignalR progress tracking module
const Progress = {
    connection: null,
    onProgressUpdate: null,

    async connect() {
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/benchmark")
            .withAutomaticReconnect()
            .build();

        this.connection.on("ProgressUpdate", (progress) => {
            if (this.onProgressUpdate) {
                this.onProgressUpdate(progress);
            }
        });

        // Listen for detailed provider progress updates
        this.connection.on("ProviderProgressUpdate", (progress) => {
            console.log("ProviderProgressUpdate received:", progress);
            console.log("providerProgress property:", progress.providerProgress);
            if (progress.providerProgress && Object.keys(progress.providerProgress).length > 0) {
                console.log("Updating provider progress with:", progress.providerProgress);
                this.updateProviderProgress(progress.providerProgress);
            } else {
                console.warn("No providerProgress data or empty object");
            }
        });

        await this.connection.start();
        await this.connection.invoke("Subscribe");
    },

    async disconnect() {
        if (this.connection) {
            await this.connection.invoke("Unsubscribe");
            await this.connection.stop();
            this.connection = null;
        }
    },

    updateUI(progress) {
        // Update circular progress
        const percent = progress.progressPercentage;
        const circumference = 502.4;
        const offset = circumference - (percent / 100) * circumference;
        document.getElementById('circularProgressBar').style.strokeDashoffset = offset;
        document.getElementById('circularProgressPercent').textContent = `${percent}%`;

        // Update live stats
        document.getElementById('liveCurrentTest').textContent = progress.currentTest || 'Initializing...';
        document.getElementById('liveCurrentProvider').textContent = progress.currentProvider?.toUpperCase() || '-';
        document.getElementById('liveSpeed').textContent = Math.round(progress.currentOperationsPerSecond).toLocaleString();
        document.getElementById('liveCurrentOps').textContent = progress.currentOperationCount.toLocaleString();
        document.getElementById('liveTotalOps').textContent = progress.totalOperations.toLocaleString();

        // Update legacy elements (for compatibility)
        document.getElementById('currentProvider').textContent = progress.currentProvider;
        document.getElementById('currentTest').textContent = progress.currentTest;
        document.getElementById('progressPercent').textContent = progress.progressPercentage;
        document.getElementById('currentOps').textContent = progress.currentOperationCount.toLocaleString();
        document.getElementById('totalOps').textContent = progress.totalOperations.toLocaleString();
        document.getElementById('currentSpeed').textContent = Math.round(progress.currentOperationsPerSecond).toLocaleString();

        // Update linear progress bar (if it exists)
        const progressBar = document.getElementById('progressBar');
        if (progressBar) {
            progressBar.style.width = `${progress.progressPercentage}%`;
        }

        // Update per-provider progress (if available)
        if (progress.providerProgress && Object.keys(progress.providerProgress).length > 0) {
            this.updateProviderProgress(progress.providerProgress);
        }

        // Add to log
        const log = document.getElementById('testLog');
        const logEntry = document.createElement('div');
        logEntry.className = 'log-entry';
        const timestamp = new Date().toLocaleTimeString();
        logEntry.innerHTML = `<strong>[${timestamp}]</strong> ${progress.currentProvider?.toUpperCase()} → ${progress.currentTest} <span style="color: var(--color-success);">${Math.round(progress.currentOperationsPerSecond).toLocaleString()} ops/sec</span>`;
        log.appendChild(logEntry);

        // Auto-scroll log
        log.scrollTop = log.scrollHeight;
    },

    updateProviderProgress(providerProgress) {
        const container = document.getElementById('providerProgressContainer');
        const barsContainer = document.getElementById('providerProgressBars');

        // Show the container
        container.style.display = 'block';

        // Create or update progress bars for each provider
        for (const [providerName, providerData] of Object.entries(providerProgress)) {
            let providerDiv = document.getElementById(`provider-${providerName}`);

            if (!providerDiv) {
                // Create new provider progress bar
                providerDiv = document.createElement('div');
                providerDiv.id = `provider-${providerName}`;
                providerDiv.className = 'provider-progress-item';

                providerDiv.innerHTML = `
                    <div class="provider-header">
                        <span class="provider-name">${this.getProviderDisplayName(providerName)}</span>
                        <span class="provider-status ${providerData.status}">${providerData.status}</span>
                        <span class="provider-percentage">0%</span>
                    </div>
                    <div class="provider-progress-bar-container">
                        <div class="provider-progress-bar" style="width: 0%"></div>
                    </div>
                    <div class="provider-current-test"></div>
                `;

                barsContainer.appendChild(providerDiv);
            }

            // Update progress bar
            const progressBar = providerDiv.querySelector('.provider-progress-bar');
            const percentage = providerData.progressPercentage || 0;
            progressBar.style.width = `${percentage}%`;

            // Update status
            const statusSpan = providerDiv.querySelector('.provider-status');
            statusSpan.textContent = providerData.status;
            statusSpan.className = `provider-status ${providerData.status}`;

            // Update percentage
            const percentageSpan = providerDiv.querySelector('.provider-percentage');
            percentageSpan.textContent = `${percentage}%`;

            // Update current test
            const currentTestDiv = providerDiv.querySelector('.provider-current-test');
            currentTestDiv.textContent = providerData.currentTest || '';

            // Color the progress bar based on status
            switch (providerData.status) {
                case 'completed':
                    progressBar.style.backgroundColor = '#4caf50';
                    break;
                case 'running':
                    progressBar.style.backgroundColor = '#2196f3';
                    break;
                case 'failed':
                    progressBar.style.backgroundColor = '#f44336';
                    break;
                default:
                    progressBar.style.backgroundColor = '#9e9e9e';
            }
        }
    },

    getProviderDisplayName(providerName) {
        const displayNames = {
            'sqlite': 'SQLite',
            'postgres': 'PostgreSQL',
            'mongo': 'MongoDB',
            'redis': 'Redis'
        };
        return displayNames[providerName] || providerName;
    },

    clearProviderProgress() {
        const container = document.getElementById('providerProgressContainer');
        const barsContainer = document.getElementById('providerProgressBars');
        container.style.display = 'none';
        barsContainer.innerHTML = '';
    }
};
