// API communication module
const API = {
    baseUrl: '',

    async getProviders() {
        const response = await fetch(`${this.baseUrl}/api/benchmark/providers`);
        return await response.json();
    },

    async getTiers() {
        const response = await fetch(`${this.baseUrl}/api/benchmark/tiers`);
        return await response.json();
    },

    async runBenchmark(config) {
        const response = await fetch(`${this.baseUrl}/api/benchmark/run`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(config)
        });

        if (!response.ok) {
            throw new Error(`Benchmark failed: ${response.statusText}`);
        }

        return await response.json();
    },

    async getJobStatus(jobId) {
        const response = await fetch(`${this.baseUrl}/api/benchmark/status/${jobId}`);

        if (!response.ok) {
            throw new Error(`Failed to get job status: ${response.statusText}`);
        }

        return await response.json();
    },

    async cancelJob(jobId) {
        const response = await fetch(`${this.baseUrl}/api/benchmark/cancel/${jobId}`, {
            method: 'POST'
        });

        if (!response.ok) {
            throw new Error(`Failed to cancel job: ${response.statusText}`);
        }

        return await response.json();
    }
};
