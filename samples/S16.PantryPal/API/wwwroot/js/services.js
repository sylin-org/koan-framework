(function(){
  'use strict';

  angular.module('pantryPal')
    .factory('ApiService', ['$http', function($http){
      var base = '';
      function buildParams(params){
        var out = {};
        angular.forEach(params || {}, function(v,k){ if(v!==undefined && v!==null && v!==''){ out[k]=v; } });
        return out;
      }
      return {
        getData: function(model, params){ return $http.get(base + '/api/data/' + model, { params: buildParams(params) }); },
        getById: function(model, id, params){ return $http.get(base + '/api/data/' + model + '/' + id, { params: buildParams(params) }); },
        patch: function(model, id, partial){ return $http.patch(base + '/api/data/' + model + '/' + id, partial, { headers: { 'Content-Type': 'application/merge-patch+json' } }); },
        put: function(model, id, payload){ return $http.put(base + '/api/data/' + model + '/' + id, payload); },
        post: function(model, payload){ return $http.post(base + '/api/data/' + model, payload); },
        uploadPhoto: function(file){
          var fd = new FormData(); fd.append('photo', file);
          return $http.post(base + '/api/action/pantry/upload', fd, { headers: { 'Content-Type': undefined } });
        },
        confirmPhoto: function(photoId, confirmations){
          return $http.post(base + '/api/action/pantry/confirm/' + encodeURIComponent(photoId), { confirmations: confirmations });
        },
        getInsights: function(){ return $http.get(base + '/api/pantry-insights/stats'); },
        suggestMeals: function(payload){ return $http.post(base + '/api/meals/suggest', payload || {}); },
        planMeals: function(payload){ return $http.post(base + '/api/meals/plan', payload || {}); },
        shoppingFromPlan: function(planId){ return $http.post(base + '/api/meals/shopping/' + encodeURIComponent(planId)); }
      };
    }])
    .factory('AuthService', ['$http', function($http){
      return {
        check: function(){
          // S5.Recs-style test provider; treat 200 as signed in
          return $http.get('/api/auth/status').then(function(r){ var d=r && r.data; return { isAuthenticated: !!(d && d.isAuthenticated) } }, function(){ return { isAuthenticated: false }; });
        }
      };
    }])
    .factory('ToastService', ['$rootScope', '$timeout', function($rootScope, $timeout){
      $rootScope.toasts = [];
      function push(type, text){ var t={ id: Date.now()+Math.random(), type:type, text:text }; $rootScope.toasts.push(t); $timeout(function(){
        var idx = $rootScope.toasts.indexOf(t); if(idx>=0) $rootScope.toasts.splice(idx,1);
      }, 3000); }
      return { success: function(m){push('success',m)}, info: function(m){push('info',m)}, warn: function(m){push('warn',m)}, error: function(m){push('error',m)} };
    }])
    .factory('RecentRequestsService', [function(){
      var buf = [];
      return {
        record: function(cfg, resp, degraded){
          buf.unshift({ ts: new Date(), cfg: cfg, resp: resp, degraded: degraded });
          if(buf.length>20) buf.pop();
        },
        list: function(){ return buf; }
      };
    }]);
})();
