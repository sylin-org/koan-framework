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
        document.getElementById('currentProvider').textContent = progress.currentProvider;
        document.getElementById('currentTest').textContent = progress.currentTest;
        document.getElementById('progressPercent').textContent = progress.progressPercentage;
        document.getElementById('currentOps').textContent = progress.currentOperationCount.toLocaleString();
        document.getElementById('totalOps').textContent = progress.totalOperations.toLocaleString();
        document.getElementById('currentSpeed').textContent = Math.round(progress.currentOperationsPerSecond).toLocaleString();

        // Update progress bar
        const progressBar = document.getElementById('progressBar');
        progressBar.style.width = `${progress.progressPercentage}%`;

        // Add to log
        const log = document.getElementById('testLog');
        const logEntry = document.createElement('div');
        logEntry.className = 'log-entry';
        logEntry.textContent = `[${progress.currentProvider}] ${progress.currentTest} - ${Math.round(progress.currentOperationsPerSecond)} ops/sec`;
        log.appendChild(logEntry);

        // Auto-scroll log
        log.scrollTop = log.scrollHeight;
    }
};
