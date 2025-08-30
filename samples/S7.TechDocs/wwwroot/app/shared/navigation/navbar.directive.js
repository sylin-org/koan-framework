(function(){
  'use strict';
  angular.module('s7.shared')
    .directive('navbar', function(){
      return {
        restrict: 'E',
        templateUrl: '/app/shared/navigation/navbar.html'
      };
    })
    .controller('NavbarController', function($scope, $window, AuthService){
      var vm = this;
      // Bind live state reference to avoid stale snapshot booleans
      vm.state = AuthService.state;

      $scope.$on('auth:changed', function(_, evt){
        // Ensure a digest runs after async broadcast
        try { $scope.$applyAsync(); } catch(_){}
      });

      vm.login = function(){
        var ret = $window.location.pathname + $window.location.search + $window.location.hash;
        AuthService.login(ret);
      };
      vm.logout = function(){
        var ret = $window.location.pathname;
        AuthService.logout(ret);
      };
    });
})();
