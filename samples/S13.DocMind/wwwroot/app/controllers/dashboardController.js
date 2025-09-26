angular.module('s13DocMindApp').controller('DashboardController', [
    '$scope', '$q', 'InsightsService', 'ToastService',
    function($scope, $q, InsightsService, ToastService) {
        $scope.loading = true;
        $scope.overview = null;
        $scope.profileCollections = [];
        $scope.activityFeed = [];

        function initialize() {
            $scope.loading = true;

            $q.all({
                overview: InsightsService.getOverview(),
                profiles: InsightsService.getProfileCollections('all'),
                feed: InsightsService.getFeeds()
            }).then(function(results) {
                $scope.overview = results.overview;
                $scope.profileCollections = results.profiles || [];
                $scope.activityFeed = (results.feed || []).slice(0, 10);
                $scope.loading = false;
                $scope.$applyAsync();
            }).catch(function(error) {
                console.error('Failed to load dashboard insights', error);
                ToastService.handleError(error, 'Failed to load insights');
                $scope.loading = false;
                $scope.$applyAsync();
            });
        }

        $scope.getCompletionRate = function() {
            if (!$scope.overview || !$scope.overview.totalDocuments) {
                return 0;
            }
            return Math.round(($scope.overview.completedDocuments / $scope.overview.totalDocuments) * 100);
        };

        $scope.getTopCollections = function() {
            return ($scope.profileCollections || []).slice(0, 3);
        };

        $scope.formatTimestamp = function(timestamp) {
            return timestamp ? new Date(timestamp) : null;
        };

        initialize();
    }
]);