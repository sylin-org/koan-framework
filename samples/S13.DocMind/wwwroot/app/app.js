angular.module('s13DocMindApp', [
    'ngRoute',
    'ngAnimate'
]).config(['$routeProvider', '$locationProvider', function($routeProvider, $locationProvider) {
    $locationProvider.hashPrefix('');

    $routeProvider
        .when('/', {
            templateUrl: 'app/views/home/index.html',
            controller: 'HomeController'
        })
        .when('/files', {
            templateUrl: 'app/views/files/list.html',
            controller: 'FilesController'
        })
        .when('/files/upload', {
            templateUrl: 'app/views/files/upload.html',
            controller: 'FileUploadController'
        })
        .when('/files/:id', {
            templateUrl: 'app/views/files/detail.html',
            controller: 'FileDetailController'
        })
        .when('/document-types', {
            templateUrl: 'app/views/document-types/list.html',
            controller: 'DocumentTypesController'
        })
        .when('/document-types/new', {
            templateUrl: 'app/views/document-types/new.html',
            controller: 'DocumentTypeNewController'
        })
        .when('/document-types/:id', {
            templateUrl: 'app/views/document-types/detail.html',
            controller: 'DocumentTypeDetailController'
        })
        .when('/analysis', {
            templateUrl: 'app/views/analysis/list.html',
            controller: 'AnalysisController'
        })
        .when('/analysis/:id', {
            templateUrl: 'app/views/analysis/detail.html',
            controller: 'AnalysisDetailController'
        })
        .when('/configuration', {
            templateUrl: 'app/views/configuration/index.html',
            controller: 'ConfigurationController'
        })
        .when('/dashboard', {
            templateUrl: 'app/views/dashboard/index.html',
            controller: 'DashboardController'
        })
        .otherwise({
            redirectTo: '/'
        });
}]).run(['$rootScope', '$location', function($rootScope, $location) {
    $rootScope.$on('$routeChangeStart', function(event, next, current) {
        console.log('Navigating to:', next.originalPath);
    });

    $rootScope.$on('$routeChangeError', function(event, current, previous, rejection) {
        console.error('Route change error:', rejection);
        $location.path('/');
    });
}]);