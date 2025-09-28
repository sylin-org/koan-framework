angular.module('s13DocMindApp').controller('AnalysisController', [
    '$scope', '$location', 'AnalysisService', 'ToastService',
    function($scope, $location, AnalysisService, ToastService) {
        $scope.loading = true;
        $scope.sessions = [];
        $scope.filteredSessions = [];
        $scope.stats = {};
        $scope.search = '';
        $scope.statusFilter = 'all';
        $scope.sortKey = 'lastRunAt';
        $scope.sortDirection = 'desc';

        function initialize() {
            $scope.loading = true;

            Promise.all([
                AnalysisService.getStats(),
                AnalysisService.getAll()
            ]).then(function(results) {
                $scope.stats = results[0] || {};
                $scope.sessions = Array.isArray(results[1]) ? results[1] : [];
                applyFilters();
                $scope.loading = false;
                $scope.$applyAsync();
            }).catch(function(error) {
                console.error('Failed to load manual analysis sessions', error);
                ToastService.handleError(error, 'Failed to load analysis sessions');
                $scope.loading = false;
                $scope.$applyAsync();
            });
        }

        function applyFilters() {
            var sessions = ($scope.sessions || []).slice();

            if ($scope.statusFilter !== 'all') {
                var status = $scope.statusFilter.toLowerCase();
                sessions = sessions.filter(function(session) {
                    return AnalysisService.normalizeStatus(session.status) === status;
                });
            }

            if ($scope.search) {
                var search = $scope.search.toLowerCase();
                sessions = sessions.filter(function(session) {
                    return session.title && session.title.toLowerCase().indexOf(search) >= 0;
                });
            }

            sessions.sort(function(a, b) {
                var direction = $scope.sortDirection === 'asc' ? 1 : -1;
                var valueA = resolveSortValue(a, $scope.sortKey);
                var valueB = resolveSortValue(b, $scope.sortKey);

                if (valueA < valueB) return -1 * direction;
                if (valueA > valueB) return 1 * direction;
                return 0;
            });

            $scope.filteredSessions = sessions;
        }

        function resolveSortValue(session, key) {
            if (!session) {
                return 0;
            }

            switch (key) {
                case 'title':
                    return (session.title || '').toLowerCase();
                case 'documents':
                    return session.documents && session.documents.length ? session.documents.length : 0;
                case 'confidence':
                    return AnalysisService.getConfidenceScore(session.lastSynthesis && session.lastSynthesis.confidence) || 0;
                case 'createdAt':
                    return session.createdAt ? new Date(session.createdAt).getTime() : 0;
                case 'lastRunAt':
                default:
                    if (session.lastRunAt) return new Date(session.lastRunAt).getTime();
                    if (session.updatedAt) return new Date(session.updatedAt).getTime();
                    return 0;
            }
        }

        $scope.setStatusFilter = function(status) {
            $scope.statusFilter = status;
            applyFilters();
        };

        $scope.setSort = function(sortKey) {
            if ($scope.sortKey === sortKey) {
                $scope.sortDirection = $scope.sortDirection === 'asc' ? 'desc' : 'asc';
            } else {
                $scope.sortKey = sortKey;
                $scope.sortDirection = sortKey === 'title' ? 'asc' : 'desc';
            }
            applyFilters();
        };

        $scope.getSortIcon = function(sortKey) {
            if ($scope.sortKey !== sortKey) {
                return 'bi-arrow-down-up';
            }
            return $scope.sortDirection === 'asc' ? 'bi-arrow-up' : 'bi-arrow-down';
        };

        $scope.createSession = function() {
            $location.path('/analysis/new');
        };

        $scope.viewSession = function(session) {
            if (!session || !session.id) {
                return;
            }
            $location.path('/analysis/' + session.id);
        };

        $scope.editSession = function(session) {
            if (!session || !session.id) {
                return;
            }
            $location.path('/analysis/' + session.id + '/edit');
        };

        $scope.runSession = function(session) {
            if (!session || !session.id) {
                return;
            }

            AnalysisService.runSession(session.id, {}).then(function(response) {
                ToastService.success('Manual analysis started');
                if (response && response.session) {
                    session.status = response.session.status;
                    session.lastRunAt = response.session.lastRunAt;
                    session.lastSynthesis = response.session.lastSynthesis;
                }
                applyFilters();
                $scope.$applyAsync();
            }).catch(function(error) {
                console.error('Failed to run manual analysis', error);
                ToastService.handleError(error, 'Failed to run analysis');
            });
        };

        $scope.deleteSession = function(session) {
            if (!session || !session.id) {
                return;
            }

            if (!confirm('Delete manual analysis "' + session.title + '"?')) {
                return;
            }

            AnalysisService.delete(session.id).then(function() {
                ToastService.success('Manual analysis deleted');
                $scope.sessions = $scope.sessions.filter(function(item) { return item.id !== session.id; });
                applyFilters();
                $scope.$applyAsync();
            }).catch(function(error) {
                console.error('Failed to delete manual analysis', error);
                ToastService.handleError(error, 'Failed to delete analysis');
            });
        };

        $scope.refresh = function() {
            initialize();
        };

        $scope.statusLabel = AnalysisService.statusLabel;
        $scope.statusClass = AnalysisService.statusClass;
        $scope.getConfidenceLabel = AnalysisService.getConfidenceLabel;
        $scope.getConfidenceClass = AnalysisService.getConfidenceClass;
        $scope.formatConfidenceScore = AnalysisService.formatConfidenceScore;
        $scope.formatDate = AnalysisService.formatDate;
        $scope.primaryFinding = AnalysisService.getPrimaryFinding;
        $scope.hasSynthesis = AnalysisService.hasSynthesis;

        $scope.$watch('search', applyFilters);

        initialize();
    }
]);
