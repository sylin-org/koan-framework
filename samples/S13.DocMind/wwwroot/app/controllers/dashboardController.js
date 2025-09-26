angular.module('s13DocMindApp').controller('DashboardController', ['$scope', 'FileService', 'DocumentTypeService', 'AnalysisService', 'ToastService', function($scope, FileService, DocumentTypeService, AnalysisService, ToastService) {
    $scope.stats = {
        totalFiles: 0,
        processedFiles: 0,
        processingFiles: 0,
        documentTypes: 0
    };

    $scope.recentActivity = [];

    function loadStats() {
        // Load file statistics
        FileService.getAll()
            .then(function(response) {
                var files = response.data || [];
                $scope.stats.totalFiles = files.length;
                $scope.stats.processedFiles = files.filter(f => f.status === 'processed').length;
                $scope.stats.processingFiles = files.filter(f => f.status === 'processing').length;

                // Generate recent activity from files
                $scope.recentActivity = files
                    .filter(f => f.uploadDate)
                    .sort((a, b) => new Date(b.uploadDate) - new Date(a.uploadDate))
                    .slice(0, 10)
                    .map(f => ({
                        description: `File "${f.name}" was uploaded`,
                        timestamp: f.uploadDate
                    }));
            })
            .catch(function(error) {
                console.error('Error loading file stats:', error);
            });

        // Load document type count
        DocumentTypeService.getAll()
            .then(function(response) {
                $scope.stats.documentTypes = (response.data || []).length;
            })
            .catch(function(error) {
                console.error('Error loading document type stats:', error);
            });
    }

    // Initialize dashboard
    loadStats();
}]);