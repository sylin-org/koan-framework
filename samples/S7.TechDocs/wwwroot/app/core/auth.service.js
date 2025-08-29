(function () {
  'use strict';

  angular.module('s7.core')
    .factory('AuthService', function ($http, $window, $q, $rootScope, $location) {
      var state = {
        isAuthenticated: false,
        user: null,
        providers: []
      };

      function broadcast() {
        try { $rootScope.$broadcast('auth:changed', { isAuthenticated: state.isAuthenticated, user: state.user }); } catch (_) { }
      }

      async function loadProviders() {
        try {
          var r = await $http.get('/.well-known/auth/providers', { withCredentials: true });
          if (r && r.data && Array.isArray(r.data.providers)) state.providers = r.data.providers.filter(function (p) { return p && p.enabled; });
        } catch (_) { state.providers = []; }
      }

      async function loadMe() {
        try {
          var r = await $http.get('/me', { withCredentials: true });
          state.user = r && r.data ? r.data : null;
          state.isAuthenticated = !!state.user;
        } catch (_) {
          state.user = null; state.isAuthenticated = false;
        }
        broadcast();
      }

      function currentUrl() {
        try { return $location.absUrl(); } catch (_) { return $window.location.href; }
      }

      function pickProvider() {
        if (state.providers.length > 0) return state.providers[0];
        // Fallback: assume local TestProvider when present
        return { id: 'test', name: 'Test Provider', protocol: 'OAuth2' };
      }

      async function login(ret) {
        if (!ret) ret = '/';
        // Ensure providers loaded at least once
        if (!state.providers || state.providers.length === 0) { try { await loadProviders(); } catch (_) { } }
        var p = pickProvider();
        var target = '/auth/' + encodeURIComponent(p.id) + '/challenge?return=' + encodeURIComponent(ret) + '&prompt=login';
        $window.location.href = target;
      }

      async function logout(ret) {
        if (!ret) ret = '/';
        $window.location.href = '/auth/logout?return=' + encodeURIComponent(ret);
      }

      async function init() {
        await $q.all([ loadProviders(), loadMe() ]);
        return state;
      }

      return {
        // state
        state: state,
        get isAuthenticated() { return state.isAuthenticated; },
        get user() { return state.user; },
        get providers() { return state.providers; },
        // actions
        init: init,
        refresh: loadMe,
        login: login,
        logout: logout
      };
    })
    .run(function (AuthService) {
      // Initialize auth state on app start
      try { AuthService.init(); } catch (_) { }
    });
})();
