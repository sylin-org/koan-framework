(function() {
    'use strict';

    angular.module('flowDashboard', [])
        .controller('MainController', MainController);

    MainController.$inject = ['$scope', '$interval', 'apiService', 'modalsService'];

    function MainController($scope, $interval, apiService, modalsService) {
        var vm = this;

        vm.adapters = [];
        vm.activityFeed = [];

        vm.openSeedModal = modalsService.openSeedModal;
        vm.openBulkImportModal = modalsService.openBulkImportModal;

        function activate() {
            getAdapters();
            $interval(getAdapters, 5000);
        }

        function getAdapters() {
            apiService.getAdapters().then(function(response) {
                vm.adapters = response.data;
            });
        }
        
        $scope.$on('new-activity', function(event, activity) {
            vm.activityFeed.unshift(activity);
            if (vm.activityFeed.length > 100) {
                vm.activityFeed.pop();
            }
        });

        activate();
    }
})();
