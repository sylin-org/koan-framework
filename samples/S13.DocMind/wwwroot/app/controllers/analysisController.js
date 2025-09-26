angular.module('s13DocMindApp').controller('AnalysisController', [
    '$scope', '$location', 'AnalysisService', 'FileService', 'DocumentTypeService', 'ToastService',
    function($scope, $location, AnalysisService, FileService, DocumentTypeService, ToastService) {

        $scope.loading = true;
        $scope.analyses = [];
        $scope.files = [];
        $scope.documentTypes = [];
        $scope.selectedAnalyses = [];
        $scope.filterText = '';
        $scope.selectedConfidence = '';
        $scope.selectedType = '';
        $scope.sortField = 'createdAt';
        $scope.sortReverse = true;

        $scope.confidenceLevels = [
            { value: '', label: 'All Confidence Levels' },
            { value: 'high', label: 'High Confidence (>80%)' },
            { value: 'medium', label: 'Medium Confidence (50-80%)' },
            { value: 'low', label: 'Low Confidence (<50%)' }
        ];

        $scope.sortOptions = [
            { value: 'createdAt', label: 'Analysis Date' },
            { value: 'confidenceScore', label: 'Confidence Score' },
            { value: 'fileName', label: 'File Name' },
            { value: 'typeName', label: 'Document Type' }
        ];

        function initialize() {
            $scope.loading = true;

            Promise.all([
                loadAnalyses(),
                loadFiles(),
                loadDocumentTypes()
            ]).then(function() {
                $scope.loading = false;
                $scope.$apply();
            }).catch(function(error) {
                console.error('Failed to initialize analysis page:', error);
                ToastService.error('Failed to load analyses');
                $scope.loading = false;
                $scope.$apply();
            });
        }

        function loadAnalyses() {
            return AnalysisService.getAllAnalyses()
                .then(function(analyses) {
                    $scope.analyses = analyses;
                    return analyses;
                });
        }

        function loadFiles() {
            return FileService.getAllFiles()
                .then(function(files) {
                    $scope.files = files;
                    return files;
                });
        }

        function loadDocumentTypes() {
            return DocumentTypeService.getAllTypes()
                .then(function(types) {
                    $scope.documentTypes = [{ id: '', name: 'All Types' }].concat(types);
                    return types;
                });
        }

        // Filter and sort functionality
        $scope.filteredAnalyses = function() {
            var filtered = $scope.analyses.map(function(analysis) {
                // Enrich analysis with file and type information
                var file = $scope.files.find(function(f) { return f.id === analysis.fileId; });
                var type = $scope.documentTypes.find(function(t) { return t.id === analysis.typeId; });

                return angular.extend({}, analysis, {
                    fileName: file ? file.displayName : 'Unknown File',
                    typeName: type ? type.name : 'Unknown Type',
                    fileState: file ? file.state : -1
                });
            });

            // Text filter
            if ($scope.filterText) {
                var searchText = $scope.filterText.toLowerCase();
                filtered = filtered.filter(function(analysis) {
                    return (analysis.fileName || '').toLowerCase().includes(searchText) ||
                           (analysis.typeName || '').toLowerCase().includes(searchText) ||
                           (analysis.extractedContext || '').toLowerCase().includes(searchText) ||
                           (analysis.summary || '').toLowerCase().includes(searchText);
                });
            }

            // Confidence filter
            if ($scope.selectedConfidence) {
                filtered = filtered.filter(function(analysis) {
                    var confidence = analysis.confidenceScore || 0;
                    switch ($scope.selectedConfidence) {
                        case 'high': return confidence > 0.8;
                        case 'medium': return confidence >= 0.5 && confidence <= 0.8;
                        case 'low': return confidence < 0.5;
                        default: return true;
                    }
                });
            }

            // Type filter
            if ($scope.selectedType) {
                filtered = filtered.filter(function(analysis) {
                    return analysis.typeId === $scope.selectedType;
                });
            }

            // Sort
            filtered.sort(function(a, b) {
                var aVal = a[$scope.sortField];
                var bVal = b[$scope.sortField];

                if (typeof aVal === 'string') {
                    aVal = aVal.toLowerCase();
                    bVal = (bVal || '').toLowerCase();
                }

                if (aVal < bVal) return $scope.sortReverse ? 1 : -1;
                if (aVal > bVal) return $scope.sortReverse ? -1 : 1;
                return 0;
            });

            return filtered;
        };

        $scope.setSortField = function(field) {
            if ($scope.sortField === field) {
                $scope.sortReverse = !$scope.sortReverse;
            } else {
                $scope.sortField = field;
                $scope.sortReverse = false;
            }
        };

        $scope.getSortIcon = function(field) {
            if ($scope.sortField !== field) {
                return 'bi-arrow-down-up';
            }
            return $scope.sortReverse ? 'bi-sort-up' : 'bi-sort-down';
        };

        // Analysis operations
        $scope.viewAnalysis = function(analysis) {
            $location.path('/analysis/' + analysis.id);
        };

        $scope.viewFile = function(analysis) {
            $location.path('/files/' + analysis.fileId);
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

        $scope.regenerateAnalysis = function(analysis) {
            if (!confirm('Are you sure you want to regenerate this analysis? The current analysis will be replaced.')) {
                return;
            }

            AnalysisService.triggerAnalysis(analysis.fileId, analysis.typeId)
                .then(function() {
                    ToastService.success('Analysis regeneration started');
                    setTimeout(function() {
                        loadAnalyses().then(function() {
                            $scope.$apply();
                        });
                    }, 2000);
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to regenerate analysis');
                });
        };

        // Bulk operations
        $scope.toggleAnalysisSelection = function(analysis) {
            var index = $scope.selectedAnalyses.indexOf(analysis.id);
            if (index === -1) {
                $scope.selectedAnalyses.push(analysis.id);
            } else {
                $scope.selectedAnalyses.splice(index, 1);
            }
        };

        $scope.isAnalysisSelected = function(analysis) {
            return $scope.selectedAnalyses.indexOf(analysis.id) !== -1;
        };

        $scope.selectAllAnalyses = function() {
            var filtered = $scope.filteredAnalyses();
            $scope.selectedAnalyses = filtered.map(function(analysis) {
                return analysis.id;
            });
        };

        $scope.clearSelection = function() {
            $scope.selectedAnalyses = [];
        };

        $scope.bulkDelete = function() {
            if ($scope.selectedAnalyses.length === 0) {
                ToastService.warning('No analyses selected');
                return;
            }

            if (!confirm('Are you sure you want to delete ' + $scope.selectedAnalyses.length + ' selected analyses?')) {
                return;
            }

            var promises = $scope.selectedAnalyses.map(function(analysisId) {
                return AnalysisService.deleteAnalysis(analysisId);
            });

            Promise.all(promises)
                .then(function() {
                    ToastService.success($scope.selectedAnalyses.length + ' analyses deleted successfully');
                    $scope.clearSelection();
                    return loadAnalyses();
                })
                .then(function() {
                    $scope.$apply();
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to delete some analyses');
                    loadAnalyses().then(function() {
                        $scope.$apply();
                    });
                });
        };

        $scope.exportSelected = function() {
            if ($scope.selectedAnalyses.length === 0) {
                ToastService.warning('No analyses selected');
                return;
            }

            AnalysisService.exportAnalyses($scope.selectedAnalyses)
                .then(function(blob) {
                    var url = window.URL.createObjectURL(blob);
                    var a = document.createElement('a');
                    a.href = url;
                    a.download = 'analyses-export-' + new Date().toISOString().split('T')[0] + '.json';
                    document.body.appendChild(a);
                    a.click();
                    document.body.removeChild(a);
                    window.URL.revokeObjectURL(url);

                    ToastService.success('Export completed successfully');
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to export analyses');
                });
        };

        // Statistics
        $scope.getStats = function() {
            var analyses = $scope.filteredAnalyses();
            var total = analyses.length;
            var highConfidence = analyses.filter(function(a) { return (a.confidenceScore || 0) > 0.8; }).length;
            var mediumConfidence = analyses.filter(function(a) { var c = a.confidenceScore || 0; return c >= 0.5 && c <= 0.8; }).length;
            var lowConfidence = analyses.filter(function(a) { return (a.confidenceScore || 0) < 0.5; }).length;

            return {
                total: total,
                highConfidence: highConfidence,
                mediumConfidence: mediumConfidence,
                lowConfidence: lowConfidence,
                averageConfidence: total > 0 ? analyses.reduce(function(sum, a) { return sum + (a.confidenceScore || 0); }, 0) / total : 0
            };
        };

        // Helper methods
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

        $scope.truncateText = function(text, maxLength) {
            if (!text) return '';
            maxLength = maxLength || 100;
            return text.length > maxLength ? text.substring(0, maxLength) + '...' : text;
        };

        $scope.refresh = function() {
            initialize();
        };

        // Initialize
        initialize();
    }
]);