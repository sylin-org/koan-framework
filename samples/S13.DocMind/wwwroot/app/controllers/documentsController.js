angular.module('s13DocMindApp').controller('DocumentsController', [
    '$scope', '$location', 'DocumentService', 'TemplateService', 'ToastService',
    function($scope, $location, DocumentService, TemplateService, ToastService) {

        $scope.loading = true;
        $scope.files = [];
        $scope.documentTypes = [];
        $scope.selectedFiles = [];
        $scope.filterText = '';
        $scope.selectedState = '';
        $scope.selectedType = '';
        $scope.sortField = 'createdAt';
        $scope.sortReverse = true;

        $scope.states = [
            { value: '', label: 'All States' },
            { value: '0', label: 'Uploaded' },
            { value: '1', label: 'Type Assigned' },
            { value: '2', label: 'Analyzing' },
            { value: '3', label: 'Analyzed' },
            { value: '4', label: 'Completed' },
            { value: '5', label: 'Failed' }
        ];

        $scope.sortOptions = [
            { value: 'fileName', label: 'File Name' },
            { value: 'createdAt', label: 'Upload Date' },
            { value: 'fileSize', label: 'File Size' },
            { value: 'state', label: 'Status' }
        ];

        function initialize() {
            $scope.loading = true;

            Promise.all([
                loadFiles(),
                loadDocumentTypes()
            ]).then(function() {
                $scope.loading = false;
                $scope.$apply();
            }).catch(function(error) {
                console.error('Failed to initialize files page:', error);
                ToastService.error('Failed to load files');
                $scope.loading = false;
                $scope.$apply();
            });
        }

        function loadFiles() {
            return DocumentService.getAll()
                .then(function(files) {
                    $scope.files = files;
                    return files;
                });
        }

        function loadDocumentTypes() {
            return TemplateService.getAll()
                .then(function(types) {
                    $scope.documentTypes = [{ id: '', name: 'All Types' }].concat(types);
                    return types;
                });
        }

        // Filter and sort functionality
        $scope.filteredFiles = function() {
            var filtered = $scope.files;

            // Text filter
            if ($scope.filterText) {
                var searchText = $scope.filterText.toLowerCase();
                filtered = filtered.filter(function(file) {
                    return (file.fileName || '').toLowerCase().includes(searchText) ||
                           (file.userFileName || '').toLowerCase().includes(searchText) ||
                           (file.notes || '').toLowerCase().includes(searchText);
                });
            }

            // State filter
            if ($scope.selectedState !== '') {
                filtered = filtered.filter(function(file) {
                    return file.state.toString() === $scope.selectedState;
                });
            }

            // Type filter
            if ($scope.selectedType) {
                filtered = filtered.filter(function(file) {
                    return file.typeId === $scope.selectedType;
                });
            }

            // Sort
            filtered.sort(function(a, b) {
                var aVal = a[$scope.sortField];
                var bVal = b[$scope.sortField];

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

        // File operations
        $scope.viewFile = function(file) {
            $location.path('/documents/' + file.id);
        };

        $scope.downloadFile = function(file) {
            window.open(DocumentService.downloadFile(file.id), '_blank');
        };

        $scope.deleteFile = function(file) {
            if (!confirm('Are you sure you want to delete "' + file.displayName + '"?')) {
                return;
            }

            DocumentService.deleteFile(file.id)
                .then(function() {
                    ToastService.success('File deleted successfully');
                    loadFiles().then(function() {
                        $scope.$apply();
                    });
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to delete file');
                });
        };

        $scope.assignType = function(file, typeId) {
            if (!typeId) {
                ToastService.warning('Please select a document type');
                return;
            }

            DocumentService.assignType(file.id, typeId, null)
                .then(function(response) {
                    ToastService.success('Document type assigned successfully');
                    loadFiles().then(function() {
                        $scope.$apply();
                    });
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to assign document type');
                });
        };

        // Bulk operations
        $scope.toggleFileSelection = function(file) {
            var index = $scope.selectedFiles.indexOf(file.id);
            if (index === -1) {
                $scope.selectedFiles.push(file.id);
            } else {
                $scope.selectedFiles.splice(index, 1);
            }
        };

        $scope.isFileSelected = function(file) {
            return $scope.selectedFiles.indexOf(file.id) !== -1;
        };

        $scope.selectAllFiles = function() {
            var filtered = $scope.filteredFiles();
            $scope.selectedFiles = filtered.map(function(file) {
                return file.id;
            });
        };

        $scope.clearSelection = function() {
            $scope.selectedFiles = [];
        };

        $scope.bulkDelete = function() {
            if ($scope.selectedFiles.length === 0) {
                ToastService.warning('No files selected');
                return;
            }

            if (!confirm('Are you sure you want to delete ' + $scope.selectedFiles.length + ' selected files?')) {
                return;
            }

            var promises = $scope.selectedFiles.map(function(fileId) {
                return DocumentService.deleteFile(fileId);
            });

            Promise.all(promises)
                .then(function() {
                    ToastService.success($scope.selectedFiles.length + ' files deleted successfully');
                    $scope.clearSelection();
                    return loadFiles();
                })
                .then(function() {
                    $scope.$apply();
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to delete some files');
                    loadFiles().then(function() {
                        $scope.$apply();
                    });
                });
        };

        // Navigation
        $scope.uploadFiles = function() {
            $location.path('/documents/upload');
        };

        // Helper methods
        $scope.formatFileSize = DocumentService.formatFileSize;
        $scope.getFileIcon = DocumentService.getFileIcon;
        $scope.getStateLabel = DocumentService.getStateLabel;
        $scope.getStateClass = DocumentService.getStateClass;

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

        $scope.canAssignType = function(file) {
            return file.state === 0; // Only uploaded files can have types assigned
        };

        $scope.refresh = function() {
            initialize();
        };

        // Initialize
        initialize();
    }
]);