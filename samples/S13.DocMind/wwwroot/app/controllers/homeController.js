angular.module('s13DocMindApp').controller('HomeController', [
    '$scope', 'DocumentService', 'TemplateService', 'AnalysisService', 'ToastService',
    function($scope, DocumentService, TemplateService, AnalysisService, ToastService) {

        $scope.loading = true;
        $scope.stats = {};
        $scope.recentFiles = [];
        $scope.recentAnalyses = [];
        $scope.documentTypes = [];

        // Initialize dashboard data
        function initializeDashboard() {
            $scope.loading = true;

            var promises = [
                loadFileStats(),
                loadRecentFiles(),
                loadRecentAnalyses(),
                loadDocumentTypes()
            ];

            Promise.all(promises)
                .then(function() {
                    $scope.loading = false;
                    $scope.$apply();
                })
                .catch(function(error) {
                    console.error('Failed to load dashboard data:', error);
                    ToastService.error('Failed to load dashboard data');
                    $scope.loading = false;
                    $scope.$apply();
                });
        }

        function loadFileStats() {
            return DocumentService.getStats()
                .then(function(stats) {
                    $scope.stats.files = stats;
                    return stats;
                })
                .catch(function(error) {
                    console.error('Failed to load document stats:', error);
                    $scope.stats.files = {
                        totalFiles: 0,
                        totalFileSize: 0,
                        processedFiles: 0,
                        pendingFiles: 0
                    };
                });
        }

        function loadRecentFiles() {
            return DocumentService.getRecent(5)
                .then(function(documents) {
                    $scope.recentFiles = documents;
                    return $scope.recentFiles;
                })
                .catch(function(error) {
                    console.error('Failed to load recent documents:', error);
                    $scope.recentFiles = [];
                });
        }

        function loadRecentAnalyses() {
            return AnalysisService.getRecent(5)
                .then(function(analyses) {
                    $scope.recentAnalyses = analyses;
                    return analyses;
                })
                .catch(function(error) {
                    console.error('Failed to load recent analyses:', error);
                    $scope.recentAnalyses = [];
                });
        }

        function loadDocumentTypes() {
            return TemplateService.getAll()
                .then(function(templates) {
                    $scope.documentTypes = templates.slice(0, 6); // Show first 6 templates
                    return templates;
                })
                .catch(function(error) {
                    console.error('Failed to load document types:', error);
                    $scope.documentTypes = [];
                });
        }

        // Helper methods
        $scope.formatFileSize = DocumentService.formatFileSize;
        $scope.getFileIcon = DocumentService.getFileIcon;
        $scope.getStateLabel = DocumentService.getStateLabel;
        $scope.getStateClass = DocumentService.getStateClass;
        $scope.getConfidenceLabel = AnalysisService.getConfidenceLabel;
        $scope.getConfidenceClass = AnalysisService.getConfidenceClass;
        $scope.formatConfidenceScore = AnalysisService.formatConfidenceScore;
        $scope.getTypeIcon = TemplateService.getTypeIcon;
        $scope.formatDate = DocumentService.formatDate;

        $scope.getTotalFileSize = function() {
            if (!$scope.stats.files) return '0 B';
            return DocumentService.formatFileSize($scope.stats.files.totalFileSize);
        };

        $scope.getProcessingRate = function() {
            if (!$scope.stats.files || $scope.stats.files.totalFiles === 0) return 0;
            return Math.round(($scope.stats.files.processedFiles / $scope.stats.files.totalFiles) * 100);
        };

        $scope.navigateToFiles = function() {
            window.location.href = '#/files';
        };

        $scope.navigateToUpload = function() {
            window.location.href = '#/files/upload';
        };

        $scope.navigateToDocumentTypes = function() {
            window.location.href = '#/document-types';
        };

        $scope.navigateToAnalysis = function() {
            window.location.href = '#/analysis';
        };

        $scope.navigateToConfiguration = function() {
            window.location.href = '#/configuration';
        };

        $scope.viewFile = function(fileId) {
            window.location.href = '#/files/' + fileId;
        };

        $scope.viewAnalysis = function(analysisId) {
            window.location.href = '#/analysis/' + analysisId;
        };

        $scope.viewDocumentType = function(typeId) {
            window.location.href = '#/document-types/' + typeId;
        };

        $scope.refresh = function() {
            initializeDashboard();
        };

        // Quick actions
        $scope.quickUpload = function() {
            var input = document.createElement('input');
            input.type = 'file';
            input.multiple = true;
            input.accept = '.pdf,.docx,.txt,.png,.jpg,.jpeg,.gif,.bmp';

            input.onchange = function(event) {
                var files = event.target.files;
                if (files.length > 0) {
                    ToastService.info('Redirecting to upload page...');
                    setTimeout(function() {
                        window.location.href = '#/files/upload';
                    }, 1000);
                }
            };

            input.click();
        };

        // Initialize
        initializeDashboard();
    }
]);