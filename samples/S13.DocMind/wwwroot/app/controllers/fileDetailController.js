angular.module('s13DocMindApp').controller('FileDetailController', [
    '$scope', '$routeParams', '$location', 'FileService', 'DocumentTypeService', 'AnalysisService', 'ToastService',
    function($scope, $routeParams, $location, FileService, DocumentTypeService, AnalysisService, ToastService) {

        $scope.loading = true;
        $scope.file = null;
        $scope.documentTypes = [];
        $scope.analyses = [];
        $scope.selectedTypeId = '';
        $scope.analysisInProgress = false;
        $scope.assigningType = false;

        var fileId = $routeParams.id;

        function initialize() {
            if (!fileId) {
                ToastService.error('Invalid file ID');
                $location.path('/files');
                return;
            }

            $scope.loading = true;

            Promise.all([
                loadFile(),
                loadDocumentTypes(),
                loadAnalyses()
            ]).then(function() {
                $scope.loading = false;
                $scope.$apply();
            }).catch(function(error) {
                console.error('Failed to load file details:', error);
                ToastService.error('Failed to load file details');
                $scope.loading = false;
                $scope.$apply();
            });
        }

        function loadFile() {
            return FileService.getFile(fileId)
                .then(function(file) {
                    $scope.file = file;
                    $scope.selectedTypeId = file.typeId || '';
                    return file;
                })
                .catch(function(error) {
                    if (error.status === 404) {
                        ToastService.error('File not found');
                        $location.path('/files');
                    }
                    throw error;
                });
        }

        function loadDocumentTypes() {
            return DocumentTypeService.getAllTypes()
                .then(function(types) {
                    $scope.documentTypes = types;
                    return types;
                });
        }

        function loadAnalyses() {
            return AnalysisService.getAnalysesByFileId(fileId)
                .then(function(analyses) {
                    $scope.analyses = analyses.sort(function(a, b) {
                        return new Date(b.createdAt) - new Date(a.createdAt);
                    });
                    return analyses;
                });
        }

        // File operations
        $scope.downloadFile = function() {
            if (!$scope.file) return;
            window.open(FileService.downloadFile($scope.file.id), '_blank');
        };

        $scope.deleteFile = function() {
            if (!$scope.file) return;

            if (!confirm('Are you sure you want to delete "' + $scope.file.displayName + '"? This action cannot be undone.')) {
                return;
            }

            FileService.deleteFile($scope.file.id)
                .then(function() {
                    ToastService.success('File deleted successfully');
                    $location.path('/files');
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to delete file');
                });
        };

        // Type assignment
        $scope.assignType = function() {
            if (!$scope.selectedTypeId) {
                ToastService.warning('Please select a document type');
                return;
            }

            if ($scope.assigningType) return;

            $scope.assigningType = true;

            FileService.assignType($scope.file.id, $scope.selectedTypeId, null)
                .then(function() {
                    ToastService.success('Document type assigned successfully');
                    return loadFile();
                })
                .then(function() {
                    $scope.assigningType = false;
                    $scope.$apply();
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to assign document type');
                    $scope.assigningType = false;
                    $scope.$apply();
                });
        };

        $scope.triggerAnalysis = function() {
            if (!$scope.file.typeId) {
                ToastService.warning('Please assign a document type before triggering analysis');
                return;
            }

            if ($scope.analysisInProgress) return;

            $scope.analysisInProgress = true;

            AnalysisService.triggerAnalysis($scope.file.id, $scope.file.typeId)
                .then(function() {
                    ToastService.success('Analysis started successfully');
                    return loadFile();
                })
                .then(function() {
                    setTimeout(function() {
                        loadAnalyses().then(function() {
                            $scope.$apply();
                        });
                    }, 2000);

                    $scope.analysisInProgress = false;
                    $scope.$apply();
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to trigger analysis');
                    $scope.analysisInProgress = false;
                    $scope.$apply();
                });
        };

        // Analysis operations
        $scope.viewAnalysis = function(analysis) {
            $location.path('/analysis/' + analysis.id);
        };

        $scope.deleteAnalysis = function(analysis) {
            if (!confirm('Are you sure you want to delete this analysis?')) {
                return;
            }

            AnalysisService.deleteAnalysis(analysis.id)
                .then(function() {
                    ToastService.success('Analysis deleted successfully');
                    return loadAnalyses();
                })
                .then(function() {
                    $scope.$apply();
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to delete analysis');
                });
        };

        // Navigation
        $scope.goBack = function() {
            $location.path('/files');
        };

        // Helper methods
        $scope.formatFileSize = FileService.formatFileSize;
        $scope.getFileIcon = FileService.getFileIcon;
        $scope.getStateLabel = FileService.getStateLabel;
        $scope.getStateClass = FileService.getStateClass;
        $scope.getConfidenceLabel = AnalysisService.getConfidenceLabel;
        $scope.getConfidenceClass = AnalysisService.getConfidenceClass;
        $scope.formatConfidenceScore = AnalysisService.formatConfidenceScore;

        $scope.formatDate = function(dateString) {
            if (!dateString) return 'Unknown';
            try {
                return new Date(dateString).toLocaleDateString() + ' ' + new Date(dateString).toLocaleTimeString();
            } catch (e) {
                return dateString;
            }
        };

        $scope.getTypeName = function(typeId) {
            if (!typeId) return 'No Type';
            var type = $scope.documentTypes.find(function(t) {
                return t.id === typeId;
            });
            return type ? type.name : 'Unknown Type';
        };

        $scope.canAssignType = function() {
            return $scope.file && $scope.file.state === 0;
        };

        $scope.canTriggerAnalysis = function() {
            return $scope.file && $scope.file.typeId && ($scope.file.state === 1 || $scope.file.state === 5);
        };

        $scope.hasAnalyses = function() {
            return $scope.analyses && $scope.analyses.length > 0;
        };

        // Initialize
        initialize();
    }
]);