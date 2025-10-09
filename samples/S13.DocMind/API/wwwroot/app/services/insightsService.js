angular.module('s13DocMindApp').service('InsightsService', ['ApiService', function(ApiService) {
    return {
        getOverview: function() {
            return ApiService.get('/insights/overview');
        },

        getProfileCollections: function(profileId) {
            if (!profileId) {
                return ApiService.get('/insights/profiles');
            }
            return ApiService.get('/insights/profiles/' + encodeURIComponent(profileId));
        },

        getFeeds: function() {
            return ApiService.get('/insights/feeds');
        }
    };
}]);
