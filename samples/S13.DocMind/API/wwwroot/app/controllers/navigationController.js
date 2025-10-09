angular.module('s13DocMindApp').controller('NavigationController', ['$scope', '$location', function($scope, $location) {
    $scope.isActive = function(route) {
        var currentPath = $location.path();

        if (route === '/') {
            return currentPath === '/';
        }

        return currentPath.startsWith(route);
    };

    $scope.navigate = function(path) {
        $location.path(path);
    };

    $scope.currentUser = {
        name: 'User',
        email: 'user@example.com'
    };

    // Watch for route changes to update active states
    $scope.$on('$routeChangeSuccess', function() {
        // Update any navigation-specific logic here
    });
}]);