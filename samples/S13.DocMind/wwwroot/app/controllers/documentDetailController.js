angular.module('s13DocMindApp').controller('DocumentDetailController', [
    '$scope', '$routeParams', '$location', 'DocumentService', 'TemplateService', 'AnalysisService', 'ToastService',
    function($scope, $routeParams, $location, DocumentService, TemplateService, AnalysisService, ToastService) {

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
                $location.path('/documents');
                return;
            }

            $scope.loading = true;

            Promise.all([
                loadFile(),
                loadDocumentTypes(),
                loadAnalyses()
            ]).then(function() {
                $scope.loading = false;
                $scope.$applyAsync();
            }).catch(function(error) {
                console.error('Failed to load file details:', error);
                ToastService.error('Failed to load file details');
                $scope.loading = false;
                $scope.$applyAsync();
            });
        }

        function loadFile() {
            return DocumentService.getById(fileId)
                .then(function(file) {
                    $scope.file = file;
                    $scope.selectedTypeId = file.typeId || file.assignedProfileId || '';
                    return file;
                })
                .catch(function(error) {
                    if (error.status === 404) {
                        ToastService.error('File not found');
                        $location.path('/documents');
                    }
                    throw error;
                });
        }

        function loadDocumentTypes() {
            return TemplateService.getAll()
                .then(function(types) {
                    $scope.documentTypes = types;
                    return types;
                });
        }

        function loadAnalyses() {
            return DocumentService.getInsights(fileId)
                .then(function(analyses) {
                    $scope.analyses = analyses.sort(function(a, b) {
                        return new Date(b.generatedAt) - new Date(a.generatedAt);
                    });
                    return analyses;
                });
        }

        function getStateValue(file) {
            if (!file) {
                return null;
            }
            if (typeof file.state !== 'undefined' && file.state !== null) {
                return file.state;
            }
            if (typeof file.status !== 'undefined' && file.status !== null) {
                return file.status;
            }
            return null;
        }

        // File operations
        $scope.downloadFile = function() {
            if (!$scope.file) return;
            window.open(DocumentService.downloadFile($scope.file.id), '_blank');
        };

        $scope.deleteFile = function() {
            if (!$scope.file) return;

            if (!confirm('Are you sure you want to delete "' + ($scope.file.displayName || $scope.file.fileName) + '"? This action cannot be undone.')) {
                return;
            }

            DocumentService.deleteFile($scope.file.id)
                .then(function() {
                    ToastService.success('File deleted successfully');
                    $location.path('/documents');
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

            DocumentService.assignType($scope.file.id, $scope.selectedTypeId)
                .then(function() {
                    ToastService.success('Document type assigned successfully');
                    return loadFile();
                })
                .then(function() {
                    $scope.assigningType = false;
                    $scope.$applyAsync();
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to assign document type');
                    $scope.assigningType = false;
                    $scope.$applyAsync();
                });
        };

        $scope.triggerAnalysis = function() {
            var profileId = ($scope.file && ($scope.file.typeId || $scope.file.assignedProfileId)) || $scope.selectedTypeId;
            if (!profileId) {
                ToastService.warning('Please assign a document type before triggering analysis');
                return;
            }

            if ($scope.analysisInProgress) return;

            $scope.analysisInProgress = true;

            DocumentService.assignProfile($scope.file.id, profileId)
                .then(function() {
                    ToastService.success('Analysis started successfully');
                    return loadFile();
                })
                .then(function() {
                    setTimeout(function() {
                        loadAnalyses().then(function() {
                            $scope.$applyAsync();
                        });
                    }, 2000);

                    $scope.analysisInProgress = false;
                    $scope.$applyAsync();
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to trigger analysis');
                    $scope.analysisInProgress = false;
                    $scope.$applyAsync();
                });
        };

        $scope.viewAnalysis = function(analysis) {
            $location.path('/analysis/' + analysis.id);
        };

        // Navigation
        $scope.goBack = function() {
            $location.path('/documents');
        };

        // Helper methods
        $scope.formatFileSize = DocumentService.formatFileSize;
        $scope.getFileIcon = DocumentService.getFileIcon;
        $scope.getStateLabel = DocumentService.getStateLabel;
        $scope.getStateClass = DocumentService.getStateClass;
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
            var state = getStateValue($scope.file);
            return state === 0 || state === null;
        };

        $scope.canTriggerAnalysis = function() {
            var state = getStateValue($scope.file);
            return !!(($scope.file && ($scope.file.typeId || $scope.file.assignedProfileId)) && (state === 1 || state === 5 || state === null));
        };

        $scope.hasAnalyses = function() {
            return $scope.analyses && $scope.analyses.length > 0;
        };

        // Initialize
        initialize();
    }
]);
