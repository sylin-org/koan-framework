// S10 DevPortal AngularJS Application
angular.module('DevPortalApp', ['ngRoute'])
    .config(['$routeProvider', '$locationProvider', function($routeProvider, $locationProvider) {
        $routeProvider
            .when('/', {
                templateUrl: 'views/demo.html',
                controller: 'DemoController'
            })
            .when('/demo', {
                templateUrl: 'views/demo.html',
                controller: 'DemoController'
            })
            .when('/articles', {
                templateUrl: 'views/articles.html',
                controller: 'ArticlesController'
            })
            .when('/technologies', {
                templateUrl: 'views/technologies.html',
                controller: 'TechnologiesController'
            })
            .when('/users', {
                templateUrl: 'views/users.html',
                controller: 'UsersController'
            })
            .otherwise({
                redirectTo: '/'
            });

        // Configure hash prefix for AngularJS 1.6+ compatibility
        $locationProvider.hashPrefix('');

        // Enable HTML5 mode for clean URLs (optional)
        // $locationProvider.html5Mode(true);
    }])
    .run(['$rootScope', function($rootScope) {
        // Global application initialization
        $rootScope.appTitle = 'S10 DevPortal - Koan Framework Demo';
        $rootScope.currentProvider = 'Auto';

        // Global alert function
        $rootScope.showAlert = function(message, type = 'info') {
            const alertArea = document.getElementById('alert-area');
            const alertId = 'alert-' + Date.now();

            const alertHtml = `
                <div id="${alertId}" class="alert alert-${type} alert-dismissible alert-floating" role="alert">
                    ${message}
                    <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
                </div>
            `;

            alertArea.insertAdjacentHTML('beforeend', alertHtml);

            // Auto-dismiss after 5 seconds
            setTimeout(() => {
                const alertElement = document.getElementById(alertId);
                if (alertElement) {
                    alertElement.remove();
                }
            }, 5000);
        };

        // Update provider indicator
        $rootScope.updateProviderIndicator = function(provider) {
            $rootScope.currentProvider = provider;
            const indicator = document.getElementById('current-provider');
            if (indicator) {
                indicator.textContent = provider;
            }
        };
    }]);