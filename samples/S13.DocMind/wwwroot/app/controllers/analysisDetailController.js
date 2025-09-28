angular.module('s13DocMindApp').controller('AnalysisDetailController', [
    '$scope', '$routeParams', '$location', 'AnalysisService', 'ToastService',
    function($scope, $routeParams, $location, AnalysisService, ToastService) {
        $scope.loading = true;
        $scope.session = null;

        function loadSession() {
            $scope.loading = true;
            AnalysisService.getById($routeParams.id).then(function(session) {
                $scope.session = session;
                $scope.loading = false;
                $scope.$applyAsync();
            }).catch(function(error) {
                console.error('Failed to load manual analysis session', error);
                ToastService.handleError(error, 'Failed to load analysis');
                $location.path('/analysis');
            });
        }

        $scope.runSession = function() {
            if (!$scope.session || !$scope.session.id) {
                return;
            }

            AnalysisService.runSession($scope.session.id, {}).then(function(response) {
                ToastService.success('Manual analysis started');
                if (response && response.session) {
                    $scope.session = response.session;
                }
                $scope.$applyAsync();
            }).catch(function(error) {
                console.error('Failed to run manual analysis', error);
                ToastService.handleError(error, 'Failed to run analysis');
            });
        };

        $scope.editSession = function() {
            if ($scope.session && $scope.session.id) {
                $location.path('/analysis/' + $scope.session.id + '/edit');
            }
        };

        $scope.deleteSession = function() {
            if (!$scope.session || !$scope.session.id) {
                return;
            }

            if (!confirm('Delete manual analysis "' + $scope.session.title + '"?')) {
                return;
            }

            AnalysisService.delete($scope.session.id).then(function() {
                ToastService.success('Manual analysis deleted');
                $location.path('/analysis');
            }).catch(function(error) {
                console.error('Failed to delete manual analysis', error);
                ToastService.handleError(error, 'Failed to delete analysis');
            });
        };

        $scope.statusLabel = AnalysisService.statusLabel;
        $scope.statusClass = AnalysisService.statusClass;
        $scope.getConfidenceLabel = AnalysisService.getConfidenceLabel;
        $scope.getConfidenceClass = AnalysisService.getConfidenceClass;
        $scope.formatConfidenceScore = AnalysisService.formatConfidenceScore;
        $scope.formatDate = AnalysisService.formatDate;
        $scope.primaryFinding = AnalysisService.getPrimaryFinding;

        $scope.hasPromptVariables = function() {
            return !!($scope.session && $scope.session.prompt && $scope.session.prompt.variables && Object.keys($scope.session.prompt.variables).length > 0);
        };

        $scope.getDocumentLabel = function(document) {
            if (!document) {
                return 'Unknown document';
            }

            if (document.displayName) {
                return document.displayName;
            }

            if (document.fileName) {
                return document.fileName;
            }

            return document.sourceDocumentId || 'Unknown document';
        };

        loadSession();
    }
]);
