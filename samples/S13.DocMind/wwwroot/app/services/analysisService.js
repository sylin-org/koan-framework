angular.module('s13DocMindApp').service('AnalysisService', ['ApiService', function(ApiService) {
    var statusLabels = {
        draft: 'Draft',
        ready: 'Ready',
        running: 'Running',
        completed: 'Completed',
        archived: 'Archived'
    };

    var statusClasses = {
        draft: 'badge bg-secondary',
        ready: 'badge bg-info',
        running: 'badge bg-warning text-dark',
        completed: 'badge bg-success',
        archived: 'badge bg-dark'
    };

    function normalizeStatus(status) {
        if (status === undefined || status === null) {
            return null;
        }

        if (typeof status === 'string') {
            return status.toLowerCase();
        }

        if (typeof status === 'number') {
            switch (status) {
                case 0: return 'draft';
                case 1: return 'ready';
                case 2: return 'running';
                case 3: return 'completed';
                case 4: return 'archived';
                default: return status.toString();
            }
        }

        return status.toString().toLowerCase();
    }

    function coerceScore(score) {
        if (score === undefined || score === null) {
            return null;
        }

        var numeric = parseFloat(score);
        if (isNaN(numeric)) {
            return null;
        }

        if (numeric < 0) numeric = 0;
        if (numeric > 1) numeric = 1;
        return numeric;
    }

    var service = {
        getAll: function() {
            return ApiService.get('/analysis');
        },

        getById: function(id) {
            return ApiService.get('/analysis/' + id);
        },

        create: function(session) {
            return ApiService.post('/analysis', session);
        },

        update: function(id, session) {
            return ApiService.put('/analysis/' + id, session);
        },

        delete: function(id) {
            return ApiService.delete('/analysis/' + id);
        },

        getRecent: function(limit) {
            return ApiService.get('/analysis/recent', { limit: limit || 5 });
        },

        getStats: function() {
            return ApiService.get('/analysis/stats');
        },

        runSession: function(id, request) {
            return ApiService.post('/analysis/' + id + '/run', request || {});
        },

        statusLabel: function(status) {
            var normalized = normalizeStatus(status);
            if (!normalized) {
                return statusLabels.draft;
            }

            return statusLabels[normalized] || status;
        },

        statusClass: function(status) {
            var normalized = normalizeStatus(status);
            if (!normalized) {
                return statusClasses.draft;
            }

            return statusClasses[normalized] || 'badge bg-secondary';
        },

        getConfidenceLabel: function(score) {
            var numeric = coerceScore(score);
            if (numeric === null) {
                return 'Unknown';
            }

            if (numeric >= 0.9) return 'Excellent';
            if (numeric >= 0.8) return 'High';
            if (numeric >= 0.7) return 'Good';
            if (numeric >= 0.6) return 'Fair';
            if (numeric >= 0.5) return 'Low';
            return 'Very Low';
        },

        getConfidenceClass: function(score) {
            var numeric = coerceScore(score);
            if (numeric === null) {
                return 'bg-secondary';
            }

            if (numeric >= 0.8) return 'bg-success';
            if (numeric >= 0.7) return 'bg-info';
            if (numeric >= 0.6) return 'bg-warning';
            return 'bg-danger';
        },

        formatConfidenceScore: function(score) {
            var numeric = coerceScore(score);
            if (numeric === null) {
                return 'N/A';
            }

            return Math.round(numeric * 100) + '%';
        },

        formatDate: function(dateString) {
            if (!dateString) {
                return 'Unknown';
            }

            var date = new Date(dateString);
            if (isNaN(date.getTime())) {
                return dateString;
            }

            return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
        },

        getPrimaryFinding: function(session) {
            if (!session) {
                return null;
            }

            if (session.primaryFinding) {
                return session.primaryFinding;
            }

            if (session.lastSynthesis) {
                if (session.lastSynthesis.findings && session.lastSynthesis.findings.length > 0) {
                    return session.lastSynthesis.findings[0].body;
                }

                if (session.lastSynthesis.filledTemplate) {
                    return session.lastSynthesis.filledTemplate;
                }

                if (session.lastSynthesis.contextSummary) {
                    return session.lastSynthesis.contextSummary;
                }
            }

            return null;
        },

        normalizeStatus: normalizeStatus,

        hasSynthesis: function(session) {
            return !!(session && session.lastSynthesis);
        }
    };

    service.getConfidenceScore = coerceScore;

    return service;
}]);
