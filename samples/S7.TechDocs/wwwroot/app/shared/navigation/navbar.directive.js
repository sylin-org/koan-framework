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
      vm.isAuthenticated = AuthService.isAuthenticated;
      vm.user = AuthService.user;

      $scope.$on('auth:changed', function(_, evt){
        vm.isAuthenticated = evt.isAuthenticated;
        vm.user = evt.user;
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
