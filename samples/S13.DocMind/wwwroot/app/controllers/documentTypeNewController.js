angular.module('s13DocMindApp').controller('DocumentTypeNewController', ['$scope', '$location', 'DocumentTypeService', 'ToastService', function($scope, $location, DocumentTypeService, ToastService) {
    $scope.documentType = {
        name: '',
        description: '',
        category: ''
    };

    $scope.saving = false;

    $scope.save = function() {
        if ($scope.documentTypeForm.$invalid) {
            ToastService.error('Please fill in all required fields.');
            return;
        }

        $scope.saving = true;

        DocumentTypeService.create($scope.documentType)
            .then(function(response) {
                ToastService.success('Document type created successfully!');
                $location.path('/document-types');
            })
            .catch(function(error) {
                console.error('Error creating document type:', error);
                ToastService.error('Failed to create document type: ' + (error.data?.message || error.message || 'Unknown error'));
            })
            .finally(function() {
                $scope.saving = false;
            });
    };
}]);