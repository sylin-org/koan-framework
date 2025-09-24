(function () {
    'use strict';

    var app = angular.module('MedTrialsApp', ['ngRoute']);

    app.config(['$routeProvider', function ($routeProvider) {
        $routeProvider
            .when('/overview', {
                templateUrl: 'views/overview.html',
                controller: 'OverviewController'
            })
            .when('/visits', {
                templateUrl: 'views/visits.html',
                controller: 'VisitsController'
            })
            .when('/safety', {
                templateUrl: 'views/safety.html',
                controller: 'SafetyController'
            })
            .when('/documents', {
                templateUrl: 'views/documents.html',
                controller: 'DocumentsController'
            })
            .otherwise({ redirectTo: '/overview' });
    }]);

    app.run(['$rootScope', '$location', function ($rootScope, $location) {
        $rootScope.isActive = function (path) {
            return $location.path() === path;
        };

        $rootScope.showAlert = function (type, message) {
            var alert = angular.element('<div class="alert alert-' + type + ' alert-dismissible fade show" role="alert"></div>');
            alert.append(document.createTextNode(message));
            alert.append('<button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>');

            var container = angular.element(document.querySelector('#alert-area'));
            container.append(alert);

            var alertInstance = null;
            if (typeof bootstrap !== 'undefined' && bootstrap.Alert) {
                alertInstance = bootstrap.Alert.getOrCreateInstance(alert[0]);
            }

            var closeAlert = function () {
                if (!alert[0] || !alert[0].parentNode) {
                    return;
                }

                try {
                    if (alertInstance) {
                        alertInstance.close();
                    } else if (typeof alert.remove === 'function') {
                        alert.remove();
                    } else if (alert[0] && alert[0].parentNode) {
                        alert[0].parentNode.removeChild(alert[0]);
                    }
                } catch (err) {
                    if (typeof alert.remove === 'function') {
                        alert.remove();
                    } else if (alert[0] && alert[0].parentNode) {
                        alert[0].parentNode.removeChild(alert[0]);
                    }
                }
            };

            setTimeout(closeAlert, 5000);
        };
    }]);
})();
