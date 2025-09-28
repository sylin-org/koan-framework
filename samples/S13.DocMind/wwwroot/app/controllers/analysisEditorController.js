angular.module('s13DocMindApp').controller('AnalysisEditorController', [
    '$scope', '$routeParams', '$location', 'AnalysisService', 'DocumentService', 'TemplateService', 'ToastService',
    function($scope, $routeParams, $location, AnalysisService, DocumentService, TemplateService, ToastService) {
        var isEdit = !!$routeParams.id;

        $scope.loading = true;
        $scope.saving = false;
        $scope.session = {
            title: '',
            description: '',
            owner: 'Analyst',
            documents: [],
            prompt: {
                instructions: '',
                variables: {}
            }
        };
        $scope.templates = [];
        $scope.documents = [];
        $scope.variableKey = '';
        $scope.variableValue = '';

        function initialize() {
            Promise.all([
                DocumentService.getAll(),
                TemplateService.getAll()
            ]).then(function(results) {
                $scope.documents = Array.isArray(results[0]) ? results[0] : [];
                $scope.templates = Array.isArray(results[1]) ? results[1] : [];

                if (isEdit) {
                    return AnalysisService.getById($routeParams.id).then(function(session) {
                        $scope.session = session || {};
                        if (!$scope.session.documents) {
                            $scope.session.documents = [];
                        }
                        if (!$scope.session.prompt) {
                            $scope.session.prompt = { instructions: '', variables: {} };
                        }
                        if (!$scope.session.prompt.variables) {
                            $scope.session.prompt.variables = {};
                        }
                        $scope.loading = false;
                        $scope.$applyAsync();
                    });
                }

                $scope.loading = false;
                $scope.$applyAsync();
            }).catch(function(error) {
                console.error('Failed to load editor prerequisites', error);
                ToastService.handleError(error, 'Failed to load editor');
                $scope.loading = false;
                $scope.$applyAsync();
            });
        }

        $scope.isDocumentSelected = function(document) {
            if (!document) {
                return false;
            }
            var entry = findDocumentEntry(document.id);
            return entry ? entry.includeInSynthesis !== false : false;
        };

        $scope.toggleDocument = function(document) {
            if (!document) {
                return;
            }

            var entry = findDocumentEntry(document.id);
            if (entry) {
                entry.includeInSynthesis = !entry.includeInSynthesis;
            } else {
                $scope.session.documents.push({
                    sourceDocumentId: document.id,
                    displayName: document.displayName || document.fileName,
                    includeInSynthesis: true,
                    notes: ''
                });
            }
        };

        $scope.addVariable = function() {
            if (!$scope.variableKey) {
                return;
            }
            if (!$scope.session.prompt.variables) {
                $scope.session.prompt.variables = {};
            }
            $scope.session.prompt.variables[$scope.variableKey] = $scope.variableValue || '';
            $scope.variableKey = '';
            $scope.variableValue = '';
        };

        $scope.removeVariable = function(key) {
            if ($scope.session.prompt && $scope.session.prompt.variables) {
                delete $scope.session.prompt.variables[key];
            }
        };

        $scope.save = function() {
            $scope.saving = true;
            var payload = angular.copy($scope.session);
            payload.documents = (payload.documents || []).filter(function(doc) {
                return doc.includeInSynthesis;
            }).map(function(doc) {
                return {
                    sourceDocumentId: doc.sourceDocumentId || doc.id,
                    displayName: doc.displayName,
                    includeInSynthesis: doc.includeInSynthesis !== false,
                    notes: doc.notes || ''
                };
            });

            if (!payload.prompt) {
                payload.prompt = { instructions: '', variables: {} };
            }
            if (!payload.prompt.variables) {
                payload.prompt.variables = {};
            }

            var action = isEdit ? AnalysisService.update(payload.id, payload) : AnalysisService.create(payload);
            action.then(function(result) {
                ToastService.success('Manual analysis session saved');
                var sessionId = (result && result.id) || payload.id;
                $location.path('/analysis/' + sessionId);
            }).catch(function(error) {
                console.error('Failed to save manual analysis session', error);
                ToastService.handleError(error, 'Failed to save analysis session');
            }).finally(function() {
                $scope.saving = false;
                $scope.$applyAsync();
            });
        };

        $scope.cancel = function() {
            if (isEdit && $scope.session && $scope.session.id) {
                $location.path('/analysis/' + $scope.session.id);
            } else {
                $location.path('/analysis');
            }
        };

        function findDocumentEntry(documentId) {
            if (!$scope.session.documents) {
                $scope.session.documents = [];
                return null;
            }

            for (var i = 0; i < $scope.session.documents.length; i++) {
                var entry = $scope.session.documents[i];
                if (!entry) {
                    continue;
                }

                var entryId = entry.sourceDocumentId || entry.id;
                if (!entryId || !documentId) {
                    continue;
                }

                if (entryId.toString().toLowerCase() === documentId.toString().toLowerCase()) {
                    return entry;
                }
            }

            return null;
        }

        initialize();
    }
]);
