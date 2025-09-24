(function () {
    'use strict';

    angular.module('MedTrialsApp').factory('apiClient', ['$http', '$q', function ($http, $q) {
        var baseUrl = '/api';

        function unwrap(promise) {
            return promise.then(function (response) {
                return response.data;
            }).catch(function (error) {
                return $q.reject(error.data || { message: 'Request failed' });
            });
        }

        return {
            getTrialSites: function () {
                return unwrap($http.get(baseUrl + '/trial-sites'));
            },
            getVisits: function (params) {
                return unwrap($http.get(baseUrl + '/participant-visits', { params: params }));
            },
            planVisits: function (payload) {
                return unwrap($http.post(baseUrl + '/participant-visits/plan-adjustments', payload));
            },
            summariseSafety: function (payload) {
                return unwrap($http.post(baseUrl + '/adverse-event-reports/summarise', payload));
            },
            ingestProtocol: function (payload) {
                return unwrap($http.post(baseUrl + '/protocol-documents/ingest', payload));
            },
            queryDocuments: function (payload) {
                return unwrap($http.post(baseUrl + '/protocol-documents/query', payload));
            }
        };
    }]);
})();
