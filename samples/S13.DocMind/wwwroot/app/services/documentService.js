angular.module('s13DocMindApp').service('DocumentService', ['ApiService', '$http', '$q', function(ApiService, $http, $q) {
    var service = {
        // EntityController<SourceDocument> provides these automatically:
        getAll: function() {
            return ApiService.get('/Documents');
        },

        getById: function(id) {
            return ApiService.get('/Documents/' + id);
        },

        create: function(document) {
            return ApiService.post('/Documents', document);
        },

        update: function(id, document) {
            return ApiService.put('/Documents/' + id, document);
        },

        delete: function(id) {
            return ApiService.delete('/Documents/' + id);
        },

        // Business-specific endpoints:
        getStats: function() {
            return ApiService.get('/Documents/stats');
        },

        getRecent: function(limit) {
            return ApiService.get('/Documents/recent?limit=' + (limit || 10));
        },

        upload: function(formData, progressCallback) {
            var deferred = $q.defer();

            $http({
                method: 'POST',
                url: '/api/Documents/upload',
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

        assignProfile: function(documentId, profileId) {
            return ApiService.post('/Documents/' + documentId + '/assign-profile', {
                profileId: profileId,
                acceptSuggestion: false
            });
        },

        getTimeline: function(documentId) {
            return ApiService.get('/Documents/' + documentId + '/timeline');
        },

        getChunks: function(documentId, includeInsights) {
            return ApiService.get('/Documents/' + documentId + '/chunks?includeInsights=' + (includeInsights || false));
        },

        getInsights: function(documentId, channel) {
            var url = '/Documents/' + documentId + '/insights';
            if (channel) {
                url += '?channel=' + encodeURIComponent(channel);
            }
            return ApiService.get(url);
        },

        downloadDocument: function(documentId) {
            // Return URL for download - EntityController<T> should provide this
            return '/api/Documents/' + documentId + '/download';
        },

        // Utility methods for UI display
        getStateLabel: function(document) {
            if (!document) return 'Unknown';

            if (document.assignedProfileId) {
                return 'Processed';
            } else {
                return 'Pending';
            }
        },

        getStateClass: function(document) {
            if (!document) return 'secondary';

            if (document.assignedProfileId) {
                return 'bg-success';
            } else {
                return 'bg-warning';
            }
        },

        getFileIcon: function(document) {
            if (!document || !document.contentType) return 'bi-file';

            var contentType = document.contentType.toLowerCase();
            if (contentType.includes('pdf')) return 'bi-file-pdf';
            if (contentType.includes('word') || contentType.includes('document')) return 'bi-file-word';
            if (contentType.includes('image')) return 'bi-file-image';
            if (contentType.includes('text')) return 'bi-file-text';

            return 'bi-file';
        },

        formatFileSize: function(bytes) {
            if (!bytes || bytes === 0) return '0 Bytes';

            var k = 1024;
            var sizes = ['Bytes', 'KB', 'MB', 'GB'];
            var i = Math.floor(Math.log(bytes) / Math.log(k));

            return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
        },

        formatDate: function(dateString) {
            if (!dateString) return 'Unknown';
            var date = new Date(dateString);
            return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
        }
    };

    return service;
}]);