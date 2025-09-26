angular.module('s13DocMindApp').controller('AnalysisDetailController', ['$scope', '$routeParams', '$location', 'AnalysisService', 'ToastService', function($scope, $routeParams, $location, AnalysisService, ToastService) {
    $scope.loading = true;
    $scope.analysis = null;

    function loadAnalysis() {
        AnalysisService.get($routeParams.id)
            .then(function(response) {
                $scope.analysis = response.data;
            })
            .catch(function(error) {
                console.error('Error loading analysis:', error);
                ToastService.error('Failed to load analysis: ' + (error.data?.message || error.message || 'Unknown error'));
                $location.path('/analysis');
            })
            .finally(function() {
                $scope.loading = false;
            });
    }

    $scope.getConfidenceClass = function(confidence) {
        if (confidence >= 0.8) return 'bg-success';
        if (confidence >= 0.6) return 'bg-warning';
        return 'bg-danger';
    };

    // Initialize
    loadAnalysis();
}]);