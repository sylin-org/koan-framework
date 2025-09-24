// Framework capability demonstration utilities
window.DevPortalDemo = {
    // Provider switching demonstration
    async switchProvider(provider) {
        try {
            const result = await DevPortalApi.switchProvider(provider);
            window.angular.element(document.body).scope().$root.updateProviderIndicator(provider);
            window.angular.element(document.body).scope().$root.showAlert(
                `Successfully switched to ${provider} provider`, 'success'
            );
            return result;
        } catch (error) {
            window.angular.element(document.body).scope().$root.showAlert(
                `Failed to switch to ${provider}: ${error.message}`, 'danger'
            );
            throw error;
        }
    },

    // Capability matrix demonstration
    async displayProviderCapabilities() {
        try {
            const capabilities = await DevPortalApi.getProviderCapabilities();
            return capabilities;
        } catch (error) {
            console.error('Failed to get capabilities:', error);
            return null;
        }
    },

    // Performance comparison across providers
    async runPerformanceComparison() {
        try {
            const comparison = await DevPortalApi.getPerformanceComparison();
            return comparison;
        } catch (error) {
            console.error('Performance comparison failed:', error);
            return null;
        }
    },

    // Bulk operations demonstration
    async demonstrateBulkOperations(count = 100) {
        try {
            const result = await DevPortalApi.bulkDemo(count);
            window.angular.element(document.body).scope().$root.showAlert(
                `Bulk demo completed: ${result.recordsUpserted} records processed in ${result.timing.totalMs.toFixed(2)}ms`,
                'success'
            );
            return result;
        } catch (error) {
            window.angular.element(document.body).scope().$root.showAlert(
                `Bulk demo failed: ${error.message}`, 'danger'
            );
            throw error;
        }
    },

    // Set routing demonstration
    async demonstrateSetRouting() {
        try {
            const published = await DevPortalApi.getArticles('published');
            const drafts = await DevPortalApi.getArticles('drafts');
            const all = await DevPortalApi.getArticles();

            return {
                published: published,
                drafts: drafts,
                all: all,
                demo: {
                    message: 'Set routing allows logical partitioning of the same entity type',
                    explanation: 'Same Article entity, different logical views via ?set= parameter'
                }
            };
        } catch (error) {
            console.error('Set routing demo failed:', error);
            return null;
        }
    },

    // Relationship navigation demonstration
    async demonstrateRelationshipNavigation() {
        try {
            const demo = await DevPortalApi.getRelationshipDemo();
            return demo;
        } catch (error) {
            console.error('Relationship demo failed:', error);
            return null;
        }
    },

    // Data seeding for demonstrations
    async seedDemoData() {
        try {
            const result = await DevPortalApi.seedDemoData();
            window.angular.element(document.body).scope().$root.showAlert(
                'Demo data seeded successfully with full relationship graph', 'success'
            );
            return result;
        } catch (error) {
            window.angular.element(document.body).scope().$root.showAlert(
                `Failed to seed demo data: ${error.message}`, 'danger'
            );
            throw error;
        }
    },

    // Clear demo data
    async clearDemoData() {
        try {
            const result = await DevPortalApi.clearDemoData();
            window.angular.element(document.body).scope().$root.showAlert(
                'Demo data cleared successfully', 'info'
            );
            return result;
        } catch (error) {
            window.angular.element(document.body).scope().$root.showAlert(
                `Failed to clear demo data: ${error.message}`, 'danger'
            );
            throw error;
        }
    },

    // Format timing data for display
    formatTiming(timingMs) {
        if (timingMs < 1000) {
            return `${timingMs.toFixed(2)}ms`;
        } else {
            return `${(timingMs / 1000).toFixed(2)}s`;
        }
    },

    // Format capabilities for display
    formatCapabilities(capabilities) {
        const formatted = {
            query: [],
            write: [],
            entities: []
        };

        if (capabilities.query) {
            if (capabilities.query.supportsLinq) formatted.query.push('LINQ Queries');
            if (capabilities.query.supportsString) formatted.query.push('String Queries');
        }

        if (capabilities.write) {
            if (capabilities.write.supportsBulkUpsert) formatted.write.push('Bulk Upsert');
            if (capabilities.write.supportsBulkDelete) formatted.write.push('Bulk Delete');
        }

        if (capabilities.entities) {
            Object.keys(capabilities.entities).forEach(key => {
                formatted.entities.push({
                    name: key,
                    description: capabilities.entities[key]
                });
            });
        }

        return formatted;
    },

    // Generate demo scenarios
    getDemoScenarios() {
        return [
            {
                title: 'Provider Switching',
                description: 'Switch between MongoDB, PostgreSQL, and SQLite with same code',
                icon: 'fas fa-exchange-alt',
                action: 'switchProviders'
            },
            {
                title: 'Bulk Operations',
                description: 'Demonstrate framework bulk capabilities with 100+ records',
                icon: 'fas fa-layer-group',
                action: 'bulkOperations'
            },
            {
                title: 'Set Routing',
                description: 'Logical data partitioning - published vs draft articles',
                icon: 'fas fa-sitemap',
                action: 'setRouting'
            },
            {
                title: 'Relationship Navigation',
                description: 'Parent/child relationships with batch loading',
                icon: 'fas fa-project-diagram',
                action: 'relationships'
            },
            {
                title: 'Performance Comparison',
                description: 'Compare query performance across providers',
                icon: 'fas fa-chart-line',
                action: 'performance'
            },
            {
                title: 'Capability Detection',
                description: 'Runtime detection of provider capabilities',
                icon: 'fas fa-cogs',
                action: 'capabilities'
            }
        ];
    }
};