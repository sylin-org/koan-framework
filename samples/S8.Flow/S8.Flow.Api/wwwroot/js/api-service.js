(function() {
    'use strict';

    angular.module('flowDashboard')
        .factory('apiService', apiService);

    apiService.$inject = ['$http'];

    function apiService($http) {
        var service = {
            getAdapters: getAdapters,
            seedAdapters: seedAdapters,
            bulkImport: bulkImport
        };

        return service;

        function getAdapters() {
            return $http.get('/api/flow/adapters');
        }

        function seedAdapters(count) {
            return $http.post('/api/flow/admin/seed', { count: count });
        }

        function bulkImport(data) {
            return $http.post('/api/flow/intake/bulk', data);
        }
    }
})();
