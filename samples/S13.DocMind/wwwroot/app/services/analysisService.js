angular.module('s13DocMindApp').service('AnalysisService', ['ApiService', function(ApiService) {
    var service = {
        // EntityController<DocumentInsight> provides these automatically:
        getAll: function() {
            return ApiService.get('/analysis');
        },

        getById: function(id) {
            return ApiService.get('/analysis/' + id);
        },

        create: function(analysis) {
            return ApiService.post('/analysis', analysis);
        },

        update: function(id, analysis) {
            return ApiService.put('/analysis/' + id, analysis);
        },

        delete: function(id) {
            return ApiService.delete('/analysis/' + id);
        },

        // Business-specific endpoint from AnalysisController:
        getRecent: function(limit) {
            return ApiService.get('/analysis/recent?limit=' + (limit || 5));
        },

        // Get insights for a specific document (from DocumentsController):
        getByDocument: function(documentId, channel) {
            var url = '/Documents/' + documentId + '/insights';
            if (channel) {
                url += '?channel=' + encodeURIComponent(channel);
            }
            return ApiService.get(url);
        },

        // Trigger analysis for a document (DocumentsController assign-profile endpoint):
        triggerAnalysis: function(documentId, profileId) {
            return ApiService.post('/Documents/' + documentId + '/assign-profile', {
                ProfileId: profileId,
                AcceptSuggestion: true
            });
        },

        // Export multiple analyses to JSON/CSV:
        exportAnalyses: function(analysisIds) {
            return ApiService.post('/analysis/export', {
                AnalysisIds: analysisIds
            }, {
                responseType: 'blob'
            });
        },

        // Helper methods for UI display
        getConfidenceLabel: function(score) {
            if (!score && score !== 0) return 'Unknown';
            if (score >= 0.9) return 'Excellent';
            if (score >= 0.8) return 'High';
            if (score >= 0.7) return 'Good';
            if (score >= 0.6) return 'Fair';
            if (score >= 0.5) return 'Low';
            return 'Very Low';
        },

        getConfidenceClass: function(score) {
            if (!score && score !== 0) return 'secondary';
            if (score >= 0.8) return 'bg-success';
            if (score >= 0.7) return 'bg-info';
            if (score >= 0.6) return 'bg-warning';
            return 'bg-danger';
        },

        formatConfidenceScore: function(score) {
            if (!score && score !== 0) return 'N/A';
            return Math.round(score * 100) + '%';
        },

        formatDate: function(dateString) {
            if (!dateString) return 'Unknown';
            var date = new Date(dateString);
            return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
        },

        parseStructuredData: function(structuredData) {
            if (!structuredData) return null;

            try {
                if (typeof structuredData === 'string') {
                    return JSON.parse(structuredData);
                }
                return structuredData;
            } catch (e) {
                console.warn('Failed to parse structured data:', e);
                return null;
            }
        },

        extractKeyValuePairs: function(structuredData) {
            var parsed = service.parseStructuredData(structuredData);
            if (!parsed) return [];

            var pairs = [];
            for (var key in parsed) {
                if (parsed.hasOwnProperty(key)) {
                    pairs.push({
                        key: key.replace(/_/g, ' ').replace(/\b\w/g, function(l) {
                            return l.toUpperCase();
                        }),
                        value: parsed[key]
                    });
                }
            }
            return pairs;
        },

        getChannelIcon: function(channel) {
            var icons = {
                'entities': 'bi-tags',
                'sections': 'bi-list-ul',
                'actions': 'bi-check-circle',
                'summary': 'bi-file-text',
                'metadata': 'bi-info-circle'
            };
            return icons[channel] || 'bi-lightbulb';
        },

        getChannelColor: function(channel) {
            var colors = {
                'entities': 'primary',
                'sections': 'info',
                'actions': 'success',
                'summary': 'secondary',
                'metadata': 'warning'
            };
            return colors[channel] || 'light';
        }
    };

    return service;
}]);