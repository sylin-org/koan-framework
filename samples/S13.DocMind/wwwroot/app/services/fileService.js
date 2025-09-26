angular.module('s13DocMindApp').service('FileService', ['ApiService', '$http', '$q', function(ApiService, $http, $q) {
    var service = {
        getAllFiles: function() {
            return ApiService.get('/files');
        },

        getFile: function(id) {
            return ApiService.get('/files/' + id);
        },

        uploadFile: function(formData, progressCallback) {
            var deferred = $q.defer();

            $http({
                method: 'POST',
                url: '/api/files/upload',
                data: formData,
                headers: {
                    'Content-Type': undefined // Let browser set content type for FormData
                },
                uploadEventHandlers: {
                    progress: progressCallback
                }
            }).then(function(response) {
                deferred.resolve(response.data);
            }).catch(function(error) {
                deferred.reject(error);
            });

            return deferred.promise;
        },

        assignType: function(fileId, typeId, userId) {
            return ApiService.put('/files/' + fileId + '/assign-type', {
                typeId: typeId,
                userId: userId
            });
        },

        getProcessingStatus: function(fileId) {
            return ApiService.get('/files/' + fileId + '/status');
        },

        getAnalysis: function(fileId) {
            return ApiService.get('/files/' + fileId + '/analysis');
        },

        getSimilarTypes: function(fileId, threshold) {
            return ApiService.get('/files/' + fileId + '/similar-types', {
                threshold: threshold || 0.8
            });
        },

        updateMetadata: function(fileId, metadata) {
            return ApiService.patch('/files/' + fileId + '/metadata', metadata);
        },

        deleteFile: function(fileId) {
            return ApiService.delete('/files/' + fileId);
        },

        getFileStats: function() {
            return ApiService.get('/files/stats');
        },

        downloadFile: function(fileId) {
            // Return URL for download
            return '/api/files/' + fileId + '/download';
        },

        // Helper methods
        getStateLabel: function(state) {
            var labels = {
                0: 'Uploaded',
                1: 'Type Assigned',
                2: 'Analyzing',
                3: 'Analyzed',
                4: 'Completed',
                5: 'Failed'
            };
            return labels[state] || 'Unknown';
        },

        getStateClass: function(state) {
            var classes = {
                0: 'secondary',
                1: 'info',
                2: 'warning',
                3: 'success',
                4: 'success',
                5: 'danger'
            };
            return classes[state] || 'secondary';
        },

        getFileIcon: function(contentType) {
            if (!contentType) return 'bi-file';

            if (contentType.includes('pdf')) return 'bi-file-pdf';
            if (contentType.includes('word') || contentType.includes('document')) return 'bi-file-word';
            if (contentType.includes('image')) return 'bi-file-image';
            if (contentType.includes('text')) return 'bi-file-text';

            return 'bi-file';
        },

        formatFileSize: function(bytes) {
            if (bytes === 0) return '0 Bytes';

            var k = 1024;
            var sizes = ['Bytes', 'KB', 'MB', 'GB'];
            var i = Math.floor(Math.log(bytes) / Math.log(k));

            return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
        }
    };

    return service;
}]);