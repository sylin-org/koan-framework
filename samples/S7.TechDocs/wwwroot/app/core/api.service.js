(function(){
  'use strict';

  angular.module('s7.core')
    .factory('ApiService', ApiService);

  function ApiService($http) {
    var base = '';

    var svc = {
      documents: {
        list: function (collection, status) {
          var params = {};
          if (collection) params.collection = collection;
          if (status) params.status = status;
          return $http.get(base + '/api/documents', { params: params }).then(r => r.data);
        },
        get: function (id) { return $http.get(base + '/api/documents/' + encodeURIComponent(id)).then(r => r.data); },
        create: function (dto) { return $http.post(base + '/api/documents', dto).then(r => r.data); },
        update: function (id, dto) { return $http.put(base + '/api/documents/' + encodeURIComponent(id), dto).then(r => r.data); },
        remove: function (id) { return $http.delete(base + '/api/documents/' + encodeURIComponent(id)).then(r => r.data); },
        trackView: function (id) { return $http.post(base + '/api/documents/' + encodeURIComponent(id) + '/view', {}).then(r => r.data); },
        new: function () { return $http.get(base + '/api/documents/new').then(r => r.data); }
      },
      collections: {
        list: function () { return $http.get(base + '/api/collections').then(r => r.data); }
      },
      moderation: {
        queue: function (page, size) { return $http.get(base + '/api/documents/moderation/queue', { params: { page: page||1, size: size||50 } }).then(r => r.data); },
        stats: function () { return $http.get(base + '/api/documents/moderation/stats').then(r => r.data); },
        submit: function (id) { return $http.post(base + '/api/documents/' + encodeURIComponent(id) + '/moderation/submit', {}).then(r => r.data); },
        withdraw: function (id) { return $http.post(base + '/api/documents/' + encodeURIComponent(id) + '/moderation/withdraw', {}).then(r => r.data); },
        approve: function (id, transform) { return $http.post(base + '/api/documents/' + encodeURIComponent(id) + '/moderation/approve', transform?{transform:transform}:{}) .then(r => r.data); },
        reject: function (id, reason) { return $http.post(base + '/api/documents/' + encodeURIComponent(id) + '/moderation/reject', { reason: reason }).then(r => r.data); },
        returns: function (id, reason) { return $http.post(base + '/api/documents/' + encodeURIComponent(id) + '/moderation/return', { reason: reason }).then(r => r.data); }
      },
      engagement: {
        isBookmarked: function (id) { return $http.get(base + '/api/engagement/bookmarks/' + encodeURIComponent(id)).then(r => r.data); },
        addBookmark: function (id) { return $http.post(base + '/api/engagement/bookmarks/' + encodeURIComponent(id), {}).then(r => r.data); },
        removeBookmark: function (id) { return $http.delete(base + '/api/engagement/bookmarks/' + encodeURIComponent(id)).then(r => r.data); },
        rate: function (id, rating) { return $http.post(base + '/api/engagement/ratings/' + encodeURIComponent(id), { rating: rating }).then(r => r.data); },
        reportIssue: function (id, type, description) { return $http.post(base + '/api/engagement/issues/' + encodeURIComponent(id), { type: type, description: description }).then(r => r.data); }
      },
      search: function (q, collection) {
        var params = { q: q };
        if (collection) params.collection = collection;
        return $http.get(base + '/api/search', { params: params }).then(r => r.data);
      }
    };

    return svc;
  }
})();
