angular.module('s13DocMindApp').controller('DocumentTypesController', [
    '$scope', '$location', 'DocumentTypeService', 'ToastService',
    function($scope, $location, DocumentTypeService, ToastService) {

        $scope.loading = true;
        $scope.documentTypes = [];
        $scope.selectedTypes = [];
        $scope.filterText = '';
        $scope.sortField = 'name';
        $scope.sortReverse = false;

        $scope.sortOptions = [
            { value: 'name', label: 'Name' },
            { value: 'createdAt', label: 'Created Date' },
            { value: 'fileCount', label: 'File Count' }
        ];

        function initialize() {
            $scope.loading = true;
            loadDocumentTypes()
                .then(function() {
                    $scope.loading = false;
                })
                .catch(function(error) {
                    console.error('Failed to load document types:', error);
                    ToastService.error('Failed to load document types');
                    $scope.loading = false;
                });
        }

        function loadDocumentTypes() {
            return DocumentTypeService.getAllTypes()
                .then(function(types) {
                    $scope.documentTypes = types;
                    return types;
                });
        }

        // Filter and sort functionality
        $scope.filteredTypes = function() {
            var filtered = $scope.documentTypes;

            // Text filter
            if ($scope.filterText) {
                var searchText = $scope.filterText.toLowerCase();
                filtered = filtered.filter(function(type) {
                    return (type.name || '').toLowerCase().includes(searchText) ||
                           (type.description || '').toLowerCase().includes(searchText) ||
                           (type.extractionPrompt || '').toLowerCase().includes(searchText);
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

        // Type operations
        $scope.viewType = function(type) {
            $location.path('/document-types/' + type.id);
        };

        $scope.editType = function(type) {
            $location.path('/document-types/' + type.id + '/edit');
        };

        $scope.deleteType = function(type) {
            if (!confirm('Are you sure you want to delete "' + type.name + '"? This action cannot be undone.')) {
                return;
            }

            DocumentTypeService.deleteType(type.id)
                .then(function() {
                    ToastService.success('Document type deleted successfully');
                    return loadDocumentTypes();
                })
                .then(function() {
                    // Scope automatically updated by AngularJS
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to delete document type');
                });
        };

        $scope.duplicateType = function(type) {
            var duplicatedType = {
                name: type.name + ' (Copy)',
                description: type.description,
                extractionPrompt: type.extractionPrompt,
                templateStructure: type.templateStructure,
                validationRules: type.validationRules,
                isActive: false
            };

            DocumentTypeService.createType(duplicatedType)
                .then(function(result) {
                    ToastService.success('Document type duplicated successfully');
                    return loadDocumentTypes();
                })
                .then(function() {
                    // Scope automatically updated by AngularJS
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to duplicate document type');
                });
        };

        // Bulk operations
        $scope.toggleTypeSelection = function(type) {
            var index = $scope.selectedTypes.indexOf(type.id);
            if (index === -1) {
                $scope.selectedTypes.push(type.id);
            } else {
                $scope.selectedTypes.splice(index, 1);
            }
        };

        $scope.isTypeSelected = function(type) {
            return $scope.selectedTypes.indexOf(type.id) !== -1;
        };

        $scope.selectAllTypes = function() {
            var filtered = $scope.filteredTypes();
            $scope.selectedTypes = filtered.map(function(type) {
                return type.id;
            });
        };

        $scope.clearSelection = function() {
            $scope.selectedTypes = [];
        };

        $scope.bulkDelete = function() {
            if ($scope.selectedTypes.length === 0) {
                ToastService.warning('No document types selected');
                return;
            }

            if (!confirm('Are you sure you want to delete ' + $scope.selectedTypes.length + ' selected document types?')) {
                return;
            }

            var promises = $scope.selectedTypes.map(function(typeId) {
                return DocumentTypeService.deleteType(typeId);
            });

            Promise.all(promises)
                .then(function() {
                    ToastService.success($scope.selectedTypes.length + ' document types deleted successfully');
                    $scope.clearSelection();
                    return loadDocumentTypes();
                })
                .then(function() {
                    // Scope automatically updated by AngularJS
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to delete some document types');
                    loadDocumentTypes().then(function() {
                        // Scope automatically updated by AngularJS
                    });
                });
        };

        $scope.bulkActivate = function() {
            if ($scope.selectedTypes.length === 0) {
                ToastService.warning('No document types selected');
                return;
            }

            var promises = $scope.selectedTypes.map(function(typeId) {
                return DocumentTypeService.updateType(typeId, { isActive: true });
            });

            Promise.all(promises)
                .then(function() {
                    ToastService.success($scope.selectedTypes.length + ' document types activated successfully');
                    $scope.clearSelection();
                    return loadDocumentTypes();
                })
                .then(function() {
                    // Scope automatically updated by AngularJS
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to activate some document types');
                });
        };

        $scope.bulkDeactivate = function() {
            if ($scope.selectedTypes.length === 0) {
                ToastService.warning('No document types selected');
                return;
            }

            var promises = $scope.selectedTypes.map(function(typeId) {
                return DocumentTypeService.updateType(typeId, { isActive: false });
            });

            Promise.all(promises)
                .then(function() {
                    ToastService.success($scope.selectedTypes.length + ' document types deactivated successfully');
                    $scope.clearSelection();
                    return loadDocumentTypes();
                })
                .then(function() {
                    // Scope automatically updated by AngularJS
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to deactivate some document types');
                });
        };

        // Navigation
        $scope.createType = function() {
            $location.path('/document-types/new');
        };

        // Helper methods
        $scope.getTypeIcon = DocumentTypeService.getTypeIcon;

        $scope.formatDate = function(dateString) {
            if (!dateString) return 'Unknown';
            try {
                return new Date(dateString).toLocaleDateString() + ' ' + new Date(dateString).toLocaleTimeString();
            } catch (e) {
                return dateString;
            }
        };

        $scope.getStatusClass = function(type) {
            return type.isActive ? 'badge bg-success' : 'badge bg-secondary';
        };

        $scope.getStatusLabel = function(type) {
            return type.isActive ? 'Active' : 'Inactive';
        };

        $scope.getFileCountText = function(type) {
            var count = type.fileCount || 0;
            return count + ' file' + (count === 1 ? '' : 's');
        };

        $scope.refresh = function() {
            initialize();
        };

        // Initialize
        initialize();
    }
]);