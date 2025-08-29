(function () {
  'use strict';

  angular.module('s7.app')
    .config(config)
    .run(run);

  function config($stateProvider, $urlRouterProvider, $locationProvider) {
    $locationProvider.html5Mode(true);

    $stateProvider
      .state('browse', {
        url: '/browse',
        templateUrl: '/app/modules/browse/browse.html',
        controller: 'BrowseController',
        controllerAs: 'vm'
      })
      .state('view', {
        url: '/view/:id',
        templateUrl: '/app/modules/view/view.html',
        controller: 'ViewController',
        controllerAs: 'vm'
      })
      .state('editNew', {
        url: '/edit/new',
        templateUrl: '/app/modules/edit/edit.html',
        controller: 'EditController',
        controllerAs: 'vm',
        resolve: {
          doc: function (ApiService) {
            return ApiService.documents.new();
          }
        }
      })
      .state('edit', {
        url: '/edit/:id',
        templateUrl: '/app/modules/edit/edit.html',
        controller: 'EditController',
        controllerAs: 'vm',
        resolve: {
          doc: function ($stateParams, ApiService) {
            return ApiService.documents.get($stateParams.id);
          }
        }
      })
      .state('moderate', {
        url: '/moderate',
        templateUrl: '/app/modules/moderate/moderate.html',
        controller: 'ModerateController',
        controllerAs: 'vm'
      });

    $urlRouterProvider.otherwise('/browse');
  }

  function run($rootScope, $location) {
    // Legacy support: /?view=ID → /view/ID
    try {
      if ($location.search().view) {
        var id = $location.search().view;
        $location.search('view', null);
        $location.path('/view/' + encodeURIComponent(id));
      }
    } catch (_) { }
  }
})();
