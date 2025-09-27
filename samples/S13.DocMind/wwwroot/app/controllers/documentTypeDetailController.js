angular.module('s13DocMindApp').controller('DocumentTypeDetailController', ['$scope', '$routeParams', '$location', 'TemplateService', 'ToastService', function($scope, $routeParams, $location, TemplateService, ToastService) {
    $scope.loading = true;
    $scope.documentType = null;

    function loadDocumentType() {
        TemplateService.getById($routeParams.id)
            .then(function(response) {
                $scope.documentType = response.data;
            })
            .catch(function(error) {
                console.error('Error loading document type:', error);
                ToastService.error('Failed to load document type: ' + (error.data?.message || error.message || 'Unknown error'));
                $location.path('/document-types');
            })
            .finally(function() {
                $scope.loading = false;
            });
    }

    $scope.edit = function() {
        // For now, just redirect to list. Could implement inline editing later.
        ToastService.info('Edit functionality coming soon!');
    };

    // Initialize
    loadDocumentType();
}]);