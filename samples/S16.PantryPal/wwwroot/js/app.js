(function(){
  'use strict';

  angular.module('pantryPal', ['ngRoute'])
    .config(['$routeProvider', '$locationProvider', '$httpProvider', function($routeProvider, $locationProvider, $httpProvider){
      // Use '#/' (no hashbang) to avoid '#!/' URLs
      $locationProvider.hashPrefix('');
      $routeProvider
        .when('/dashboard', { templateUrl: 'templates/dashboard.html', controller: 'DashboardCtrl', controllerAs: 'vm' })
        .when('/pantry', { templateUrl: 'templates/pantry.html', controller: 'PantryCtrl', controllerAs: 'vm' })
        .when('/capture', { templateUrl: 'templates/capture.html', controller: 'CaptureCtrl', controllerAs: 'vm' })
        .when('/review', { templateUrl: 'templates/review.html', controller: 'ReviewCtrl', controllerAs: 'vm' })
        .when('/confirm/:photoId', { templateUrl: 'templates/confirm.html', controller: 'ConfirmCtrl', controllerAs: 'vm' })
        .when('/meals', { templateUrl: 'templates/meals.html', controller: 'MealsCtrl', controllerAs: 'vm' })
        .when('/shopping-list', { templateUrl: 'templates/shopping-list.html', controller: 'ShoppingCtrl', controllerAs: 'vm' })
        .when('/insights', { templateUrl: 'templates/insights.html', controller: 'InsightsCtrl', controllerAs: 'vm' })
        .when('/behind-the-scenes', { templateUrl: 'templates/behind.html', controller: 'BehindCtrl', controllerAs: 'vm' })
        .otherwise({ redirectTo: '/dashboard' });

      // capture headers for degraded search, request log
      $httpProvider.interceptors.push(['$q','RecentRequestsService', function($q, RecentRequestsService){
        return {
          response: function(response){
            var degraded = (response.headers && response.headers('X-Search-Degraded')) === '1';
            RecentRequestsService.record(response.config, response, degraded);
            return response;
          },
          responseError: function(rejection){
            RecentRequestsService.record(rejection.config, rejection, false);
            return $q.reject(rejection);
          }
        };
      }]);
    }])
    .run(['$rootScope','AuthService', function($rootScope, AuthService){
      $rootScope.ui = { capOverlay: false };
      $rootScope.auth = { readOnly: true };
      AuthService.check().then(function(state){ $rootScope.auth.readOnly = !state.isAuthenticated; });
    }]);
})();
