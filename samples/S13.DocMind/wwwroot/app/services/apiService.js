angular.module('s13DocMindApp').service('ApiService', ['$http', '$q', function($http, $q) {
    var baseUrl = '/api';

    var service = {
        request: function(config) {
            var deferred = $q.defer();

            // Add base URL
            config.url = baseUrl + config.url;

            // Set default headers
            config.headers = config.headers || {};
            config.headers['Content-Type'] = config.headers['Content-Type'] || 'application/json';

            $http(config)
                .then(function(response) {
                    deferred.resolve(response.data);
                })
                .catch(function(error) {
                    console.error('API Error:', error);
                    var errorMessage = 'An error occurred';

                    if (error.data && error.data.message) {
                        errorMessage = error.data.message;
                    } else if (error.data && typeof error.data === 'string') {
                        errorMessage = error.data;
                    } else if (error.statusText) {
                        errorMessage = error.statusText;
                    }

                    deferred.reject({
                        status: error.status,
                        message: errorMessage,
                        data: error.data
                    });
                });

            return deferred.promise;
        },

        get: function(url, params) {
            return service.request({
                method: 'GET',
                url: url,
                params: params
            });
        },

        post: function(url, data, headers) {
            return service.request({
                method: 'POST',
                url: url,
                data: data,
                headers: headers
            });
        },

        put: function(url, data) {
            return service.request({
                method: 'PUT',
                url: url,
                data: data
            });
        },

        patch: function(url, data) {
            return service.request({
                method: 'PATCH',
                url: url,
                data: data
            });
        },

        delete: function(url) {
            return service.request({
                method: 'DELETE',
                url: url
            });
        }
    };

    return service;
}]);