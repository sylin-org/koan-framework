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
                $scope.overview = results.overview || {};

                // Ensure profileCollections is always an array
                var profiles = results.profiles;
                $scope.profileCollections = Array.isArray(profiles) ? profiles : [];

                // Ensure activityFeed is always an array
                var feed = results.feed;
                $scope.activityFeed = Array.isArray(feed) ? feed.slice(0, 10) : [];

                $scope.loading = false;
                $scope.$applyAsync();
            }).catch(function(error) {
                console.error('Failed to load dashboard insights', error);
                ToastService.handleError(error, 'Failed to load insights');

                // Initialize with safe defaults on error
                $scope.overview = {};
                $scope.profileCollections = [];
                $scope.activityFeed = [];

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
            var collections = $scope.profileCollections;
            return Array.isArray(collections) ? collections.slice(0, 3) : [];
        };

        $scope.formatTimestamp = function(timestamp) {
            return timestamp ? new Date(timestamp) : null;
        };

        initialize();
    }
]);