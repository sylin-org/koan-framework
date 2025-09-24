(function () {
  'use strict';

  angular.module('s9Location', ['ngRoute'])
    .constant('config', {
      apiBase: '/api/location'
    })
    .config(['$routeProvider', function ($routeProvider) {
      $routeProvider
        .when('/', {
          templateUrl: 'app/dashboard.html',
          controller: 'DashboardController',
          controllerAs: 'vm'
        })
        .otherwise('/');
    }])
    .filter('percent', function () {
      return function (value, digits) {
        if (value === null || value === undefined) return '—';
        const pct = value * 100;
        return pct.toFixed(digits ?? 1) + '%';
      };
    });
})();
