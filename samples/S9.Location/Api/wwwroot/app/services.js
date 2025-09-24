(function () {
  'use strict';

  angular.module('s9Location')
    .service('apiClient', ['$http', 'config', function ($http, config) {
      const base = config.apiBase;

      function unwrap(response) {
        return response.data;
      }

      this.getMetrics = function () {
        return $http.get(base + '/metrics').then(unwrap);
      };

      this.getCanonical = function (pageSize) {
        return $http.get(base + '/canonical', { params: { pageSize } }).then(unwrap);
      };

      this.getCache = function (pageSize) {
        return $http.get(base + '/cache', { params: { pageSize } }).then(unwrap);
      };

      this.getParked = function (pageSize) {
        return $http.get(base + '/parked', { params: { pageSize } }).then(unwrap);
      };

      this.getOptions = function () {
        return $http.get(base + '/options').then(unwrap);
      };
    }]);
})();
