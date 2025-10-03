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
    }
};
