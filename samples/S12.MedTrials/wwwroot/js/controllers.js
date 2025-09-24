(function () {
    'use strict';

    angular.module('MedTrialsApp')
        .controller('OverviewController', ['$scope', 'apiClient', function ($scope, apiClient) {
            $scope.loading = true;
            $scope.sites = [];
            $scope.visitSummary = null;

            function summariseVisits(visits) {
                if (!Array.isArray(visits)) {
                    return null;
                }

                var byStatus = visits.reduce(function (acc, visit) {
                    var status = visit.status || visit.Status || 'Unknown';
                    acc[status] = (acc[status] || 0) + 1;
                    return acc;
                }, {});

                return {
                    total: visits.length,
                    byStatus: byStatus
                };
            }

            function load() {
                $scope.loading = true;
                apiClient.getTrialSites()
                    .then(function (data) {
                        $scope.sites = data.items || data;
                        return apiClient.getVisits({ pageSize: 100 });
                    })
                    .then(function (visits) {
                        var list = visits.items || visits;
                        $scope.visitSummary = summariseVisits(list);
                    })
                    .catch(function (error) {
                        console.error('Overview load failed', error);
                        $scope.$root.showAlert('danger', (error && error.message) || 'Failed to load overview data.');
                    })
                    .finally(function () {
                        $scope.loading = false;
                    });
            }

            load();
        }])
        .controller('VisitsController', ['$scope', 'apiClient', function ($scope, apiClient) {
            $scope.loading = false;
            $scope.visits = [];
            $scope.sites = [];
            $scope.form = {
                trialSiteId: null,
                participantIds: '',
                maxVisitsPerDay: 12,
                allowWeekendVisits: false
            };
            $scope.result = null;

            apiClient.getTrialSites()
                .then(function (data) {
                    $scope.sites = data.items || data;
                    if (!$scope.form.trialSiteId && $scope.sites.length > 0) {
                        var first = $scope.sites[0];
                        $scope.form.trialSiteId = first.id || first.Id;
                    }
                })
                .catch(function (error) {
                    console.error('Failed to load trial sites', error);
                    $scope.$root.showAlert('danger', (error && error.message) || 'Failed to load trial sites.');
                });

            function refreshVisits() {
                $scope.loading = true;
                var params = {};
                if ($scope.form.trialSiteId) params.trialSiteId = $scope.form.trialSiteId;
                apiClient.getVisits(params)
                    .then(function (data) {
                        $scope.visits = data.items || data;
                    })
                    .catch(function (error) {
                        console.error('Failed to load visits', error);
                        $scope.$root.showAlert('danger', (error && error.message) || 'Failed to load visits.');
                    })
                    .finally(function () {
                        $scope.loading = false;
                    });
            }

            $scope.plan = function () {
                if (!$scope.form.trialSiteId) {
                    $scope.$root.showAlert('warning', 'Select a trial site before planning.');
                    return;
                }

                $scope.loading = true;
                var payload = {
                    trialSiteId: $scope.form.trialSiteId,
                    participantIds: $scope.form.participantIds ? $scope.form.participantIds.split(',').map(function (p) { return p.trim(); }).filter(Boolean) : [],
                    maxVisitsPerDay: $scope.form.maxVisitsPerDay,
                    allowWeekendVisits: $scope.form.allowWeekendVisits
                };

                apiClient.planVisits(payload)
                    .then(function (result) {
                        $scope.result = result;
                        $scope.$root.showAlert(result.degraded ? 'warning' : 'success', 'Planner executed. Review proposed adjustments below.');
                        refreshVisits();
                    })
                    .catch(function (error) {
                        console.error('Planner failed', error);
                        $scope.$root.showAlert('danger', (error && error.message) || 'Planner failed.');
                    })
                    .finally(function () {
                        $scope.loading = false;
                    });
            };

            refreshVisits();
        }])
        .controller('SafetyController', ['$scope', 'apiClient', function ($scope, apiClient) {
            $scope.loading = false;
            $scope.form = {
                lookbackDays: 14,
                trialSiteId: null,
                minimumSeverity: null
            };
            $scope.summary = null;
            $scope.events = [];

            $scope.summarise = function () {
                $scope.loading = true;
                apiClient.summariseSafety({
                    lookbackDays: $scope.form.lookbackDays,
                    trialSiteId: $scope.form.trialSiteId,
                    minimumSeverity: $scope.form.minimumSeverity
                }).then(function (result) {
                    $scope.summary = result;
                    $scope.events = result.events || [];
                    $scope.$root.showAlert(result.degraded ? 'warning' : 'success', 'Safety digest updated.');
                }).catch(function (error) {
                    console.error('Safety digest failed', error);
                    $scope.$root.showAlert('danger', (error && error.message) || 'Failed to summarise safety events.');
                }).finally(function () {
                    $scope.loading = false;
                });
            };

            $scope.summarise();
        }])
        .controller('DocumentsController', ['$scope', 'apiClient', function ($scope, apiClient) {
            $scope.loading = false;
            $scope.documents = [];
            $scope.ingestModel = {
                title: '',
                documentType: 'Protocol',
                content: '',
                trialSiteId: '',
                tags: ''
            };
            $scope.queryModel = {
                query: '',
                trialSiteId: '',
                includeContent: false
            };
            $scope.queryResults = null;

            function loadDocuments() {
                $scope.loading = true;
                apiClient.queryDocuments({ query: '', includeContent: false, topK: 10 })
                    .then(function (result) {
                        $scope.documents = (result.matches || []).map(function (match) { return match.document || match.Document || match; });
                    })
                    .catch(function (error) {
                        console.error('Failed to load documents', error);
                        $scope.$root.showAlert('danger', (error && error.message) || 'Failed to load documents.');
                    })
                    .finally(function () {
                        $scope.loading = false;
                    });
            }

            $scope.ingest = function () {
                if (!$scope.ingestModel.content) {
                    $scope.$root.showAlert('warning', 'Provide document content before ingesting.');
                    return;
                }

                $scope.loading = true;
                var payload = {
                    title: $scope.ingestModel.title,
                    documentType: $scope.ingestModel.documentType,
                    content: $scope.ingestModel.content,
                    trialSiteId: $scope.ingestModel.trialSiteId || null,
                    tags: $scope.ingestModel.tags ? $scope.ingestModel.tags.split(',').map(function (t) { return t.trim(); }).filter(Boolean) : []
                };

                apiClient.ingestProtocol(payload)
                    .then(function (result) {
                        $scope.$root.showAlert(result.degraded ? 'warning' : 'success', 'Document ingested.');
                        $scope.ingestModel = { title: '', documentType: 'Protocol', content: '', trialSiteId: '', tags: '' };
                        loadDocuments();
                    })
                    .catch(function (error) {
                        console.error('Ingest failed', error);
                        $scope.$root.showAlert('danger', (error && error.message) || 'Failed to ingest document.');
                    })
                    .finally(function () {
                        $scope.loading = false;
                    });
            };

            $scope.search = function () {
                if (!$scope.queryModel.query) {
                    $scope.$root.showAlert('warning', 'Enter a query to perform semantic search.');
                    return;
                }

                $scope.loading = true;
                apiClient.queryDocuments({
                    query: $scope.queryModel.query,
                    trialSiteId: $scope.queryModel.trialSiteId || null,
                    includeContent: $scope.queryModel.includeContent,
                    topK: 8
                }).then(function (result) {
                    $scope.queryResults = result;
                    $scope.$root.showAlert(result.degraded ? 'warning' : 'success', 'Query executed.');
                }).catch(function (error) {
                    console.error('Query failed', error);
                    $scope.$root.showAlert('danger', (error && error.message) || 'Failed to query documents.');
                }).finally(function () {
                    $scope.loading = false;
                });
            };

            loadDocuments();
        }]);
})();
