(function () {
  'use strict';

  angular.module('s9Location')
    .controller('DashboardController', ['$interval', 'apiClient', function ($interval, apiClient) {
      var vm = this;

      vm.loading = true;
      vm.error = null;
      vm.metrics = null;
      vm.canonical = [];
      vm.cache = [];
      vm.parked = [];
      vm.options = null;
      vm.aiAssistCount = 0;
      vm.aiAssistRatio = 0;
      vm.autoRefresh = true;
      vm.lastUpdated = null;

      var refreshInterval = 15000;
      var timerHandle = null;

      vm.$onInit = function () {
        loadAll();
        timerHandle = $interval(function () {
          if (vm.autoRefresh) {
            loadAll();
          }
        }, refreshInterval);
      };

      vm.$onDestroy = function () {
        if (timerHandle) {
          $interval.cancel(timerHandle);
        }
      };

      vm.refreshNow = function () {
        loadAll();
      };

      vm.toggleAutoRefresh = function () {
        vm.autoRefresh = !vm.autoRefresh;
        if (vm.autoRefresh) {
          loadAll();
        }
      };

      function loadAll() {
        vm.loading = true;
        vm.error = null;

        Promise.all([
          apiClient.getMetrics(),
          apiClient.getCanonical(10),
          apiClient.getCache(10),
          apiClient.getParked(10),
          apiClient.getOptions()
        ]).then(function (results) {
          vm.metrics = results[0];
          vm.canonical = mapCanonical(results[1] || []);
          vm.cache = results[2] || [];
          vm.parked = results[3] || [];
          vm.options = normalizeOptions(results[4] || {});

          vm.aiAssistCount = vm.canonical.filter(function (c) { return c.aiAssistUsed; }).length;
          vm.aiAssistRatio = vm.canonical.length ? vm.aiAssistCount / vm.canonical.length : 0;
          vm.lastUpdated = new Date();
        }).catch(function (err) {
          console.error('Failed to load data', err);
          vm.error = err && err.message ? err.message : 'Failed to load data.';
        }).finally(function () {
          vm.loading = false;
        });
      }

      function mapCanonical(items) {
        return items.map(function (c) {
          c.attributes = c.attributes || {};
          c.attributes.tokens = c.attributes.tokens || {};
          c.topTokens = Object.entries(c.attributes.tokens).slice(0, 5);
          var aiMeta = c.attributes.ai_assist;
          c.aiAssistUsed = !!(aiMeta && aiMeta.used);
          c.aiAssistModel = aiMeta && aiMeta.model;
          return c;
        });
      }

      function normalizeOptions(opts) {
        var ai = opts.aiAssistEnabled !== undefined ? opts.aiAssistEnabled : (opts.aiAssist?.enabled ?? false);
        var model = opts.aiAssistModel || opts.aiAssist?.model || 'auto';
        var threshold = opts.aiConfidenceThreshold || opts.aiAssist?.confidenceThreshold || 0;
        return {
          defaultCountry: opts.defaultCountry || 'US',
          aiAssistEnabled: !!ai,
          aiAssistModel: model,
          aiConfidenceThreshold: threshold
        };
      }
    }]);
})();
