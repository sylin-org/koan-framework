angular.module('s13DocMindApp').controller('FileUploadController', [
    '$scope', '$location', 'FileService', 'ToastService',
    function($scope, $location, FileService, ToastService) {

        $scope.uploading = false;
        $scope.uploadProgress = {};
        $scope.uploadedFiles = [];
        $scope.supportedTypes = [
            '.pdf', '.docx', '.txt', '.png', '.jpg', '.jpeg', '.gif', '.bmp'
        ];

        $scope.maxFileSize = 50 * 1024 * 1024; // 50MB
        $scope.dragOver = false;

        // Drag and drop handlers
        $scope.handleDragOver = function(event) {
            event.preventDefault();
            $scope.dragOver = true;
            $scope.$apply();
        };

        $scope.handleDragLeave = function(event) {
            event.preventDefault();
            $scope.dragOver = false;
            $scope.$apply();
        };

        $scope.handleDrop = function(event) {
            event.preventDefault();
            $scope.dragOver = false;

            var files = event.dataTransfer.files;
            if (files.length > 0) {
                $scope.processFiles(Array.from(files));
            }
            $scope.$apply();
        };

        // File selection handler
        $scope.handleFileSelect = function(event) {
            var files = event.target.files;
            if (files.length > 0) {
                $scope.processFiles(Array.from(files));
            }
        };

        // Process selected files
        $scope.processFiles = function(files) {
            var validFiles = [];
            var errors = [];

            files.forEach(function(file) {
                var validation = $scope.validateFile(file);
                if (validation.valid) {
                    validFiles.push(file);
                } else {
                    errors = errors.concat(validation.errors);
                }
            });

            if (errors.length > 0) {
                ToastService.error('File validation errors: ' + errors.join(', '));
            }

            if (validFiles.length > 0) {
                $scope.uploadFiles(validFiles);
            }
        };

        // Validate individual file
        $scope.validateFile = function(file) {
            var errors = [];

            // Check file size
            if (file.size > $scope.maxFileSize) {
                errors.push(file.name + ' is too large (max ' + $scope.formatFileSize($scope.maxFileSize) + ')');
            }

            // Check file type
            var extension = '.' + file.name.split('.').pop().toLowerCase();
            if ($scope.supportedTypes.indexOf(extension) === -1) {
                errors.push(file.name + ' has unsupported file type');
            }

            return {
                valid: errors.length === 0,
                errors: errors
            };
        };

        // Upload files
        $scope.uploadFiles = function(files) {
            if ($scope.uploading) {
                ToastService.warning('Upload already in progress');
                return;
            }

            $scope.uploading = true;
            $scope.uploadProgress = {};
            $scope.uploadedFiles = [];

            var uploadPromises = files.map(function(file, index) {
                return $scope.uploadSingleFile(file, index);
            });

            Promise.all(uploadPromises)
                .then(function(results) {
                    $scope.uploading = false;
                    $scope.uploadProgress = {};

                    var successCount = results.filter(function(r) { return r.success; }).length;
                    var failCount = results.length - successCount;

                    if (successCount > 0) {
                        ToastService.success(successCount + ' file(s) uploaded successfully');
                    }

                    if (failCount > 0) {
                        ToastService.error(failCount + ' file(s) failed to upload');
                    }

                    $scope.$apply();
                })
                .catch(function(error) {
                    console.error('Upload error:', error);
                    $scope.uploading = false;
                    $scope.uploadProgress = {};
                    ToastService.error('Upload failed');
                    $scope.$apply();
                });
        };

        // Upload single file
        $scope.uploadSingleFile = function(file, index) {
            return new Promise(function(resolve) {
                var formData = new FormData();
                formData.append('file', file);

                var progressCallback = function(event) {
                    if (event.lengthComputable) {
                        var percentComplete = Math.round((event.loaded / event.total) * 100);
                        $scope.uploadProgress[index] = {
                            fileName: file.name,
                            progress: percentComplete,
                            loaded: event.loaded,
                            total: event.total
                        };
                        $scope.$apply();
                    }
                };

                FileService.uploadFile(formData, progressCallback)
                    .then(function(result) {
                        $scope.uploadedFiles.push({
                            file: result,
                            success: true
                        });
                        resolve({ success: true, file: result });
                    })
                    .catch(function(error) {
                        console.error('Failed to upload file:', file.name, error);
                        $scope.uploadedFiles.push({
                            fileName: file.name,
                            success: false,
                            error: error.message || 'Upload failed'
                        });
                        resolve({ success: false, error: error });
                    });
            });
        };

        // Navigation and actions
        $scope.viewUploadedFile = function(file) {
            $location.path('/files/' + file.id);
        };

        $scope.goToFiles = function() {
            $location.path('/files');
        };

        $scope.uploadMore = function() {
            // Clear current state
            $scope.uploadedFiles = [];
            $scope.uploadProgress = {};

            // Trigger file input
            var fileInput = document.getElementById('fileInput');
            if (fileInput) {
                fileInput.value = '';
                fileInput.click();
            }
        };

        $scope.clearUploaded = function() {
            $scope.uploadedFiles = [];
            $scope.uploadProgress = {};
        };

        // Helper methods
        $scope.formatFileSize = FileService.formatFileSize;
        $scope.getFileIcon = FileService.getFileIcon;

        $scope.getOverallProgress = function() {
            var progressValues = Object.values($scope.uploadProgress);
            if (progressValues.length === 0) return 0;

            var totalProgress = progressValues.reduce(function(sum, p) {
                return sum + p.progress;
            }, 0);

            return Math.round(totalProgress / progressValues.length);
        };

        $scope.isUploadComplete = function() {
            return $scope.uploadedFiles.length > 0 && !$scope.uploading;
        };

        $scope.getSuccessCount = function() {
            return $scope.uploadedFiles.filter(function(f) {
                return f.success;
            }).length;
        };

        $scope.getFailCount = function() {
            return $scope.uploadedFiles.filter(function(f) {
                return !f.success;
            }).length;
        };

        $scope.getSupportedTypesText = function() {
            return $scope.supportedTypes.join(', ');
        };

        $scope.getMaxFileSizeText = function() {
            return $scope.formatFileSize($scope.maxFileSize);
        };

        // Initialize drop zone event listeners
        function initializeDropZone() {
            var dropZone = document.getElementById('dropZone');
            if (dropZone) {
                dropZone.addEventListener('dragover', $scope.handleDragOver);
                dropZone.addEventListener('dragleave', $scope.handleDragLeave);
                dropZone.addEventListener('drop', $scope.handleDrop);
            }
        }

        // Cleanup on destroy
        $scope.$on('$destroy', function() {
            var dropZone = document.getElementById('dropZone');
            if (dropZone) {
                dropZone.removeEventListener('dragover', $scope.handleDragOver);
                dropZone.removeEventListener('dragleave', $scope.handleDragLeave);
                dropZone.removeEventListener('drop', $scope.handleDrop);
            }
        });

        // Initialize after view loads
        setTimeout(initializeDropZone, 100);
    }
]);