angular.module('s13DocMindApp').service('AnalysisService', ['ApiService', function(ApiService) {
    var service = {
        getAllAnalyses: function() {
            return ApiService.get('/analysis');
        },

        getAnalysis: function(id) {
            return ApiService.get('/analysis/' + id);
        },

        getAnalysesByType: function(typeId) {
            return ApiService.get('/analysis/by-type/' + typeId);
        },

        getAnalysisByFile: function(fileId) {
            return ApiService.get('/analysis/by-file/' + fileId);
        },

        getHighConfidenceAnalyses: function(threshold) {
            return ApiService.get('/analysis/high-confidence', {
                threshold: threshold || 0.8
            });
        },

        getRecentAnalyses: function(limit) {
            return ApiService.get('/analysis/recent', {
                limit: limit || 20
            });
        },

        getAnalysisStats: function() {
            return ApiService.get('/analysis/stats');
        },

        getDetailedAnalysis: function(id) {
            return ApiService.get('/analysis/' + id + '/detailed');
        },

        regenerateAnalysis: function(id) {
            return ApiService.post('/analysis/' + id + '/regenerate');
        },

        deleteAnalysis: function(id) {
            return ApiService.delete('/analysis/' + id);
        },

        // Helper methods
        getConfidenceLabel: function(score) {
            if (score >= 0.9) return 'Excellent';
            if (score >= 0.8) return 'High';
            if (score >= 0.7) return 'Good';
            if (score >= 0.6) return 'Fair';
            if (score >= 0.5) return 'Low';
            return 'Very Low';
        },

        getConfidenceClass: function(score) {
            if (score >= 0.8) return 'success';
            if (score >= 0.7) return 'info';
            if (score >= 0.6) return 'warning';
            return 'danger';
        },

        formatConfidenceScore: function(score) {
            return Math.round(score * 100) + '%';
        },

        formatProcessingTime: function(duration) {
            // Duration is in format "00:00:05.1234567" (TimeSpan)
            if (!duration) return 'Unknown';

            try {
                var parts = duration.split(':');
                var hours = parseInt(parts[0]);
                var minutes = parseInt(parts[1]);
                var seconds = parseFloat(parts[2]);

                if (hours > 0) {
                    return hours + 'h ' + minutes + 'm ' + Math.round(seconds) + 's';
                } else if (minutes > 0) {
                    return minutes + 'm ' + Math.round(seconds) + 's';
                } else {
                    return seconds.toFixed(1) + 's';
                }
            } catch (e) {
                return duration;
            }
        },

        formatTokenUsage: function(inputTokens, outputTokens) {
            var total = (inputTokens || 0) + (outputTokens || 0);
            if (total === 0) return 'No token data';

            return 'In: ' + (inputTokens || 0).toLocaleString() +
                   ', Out: ' + (outputTokens || 0).toLocaleString() +
                   ' (Total: ' + total.toLocaleString() + ')';
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
        }
    };

    return service;
}]);