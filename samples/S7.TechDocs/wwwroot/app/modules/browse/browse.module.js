(function(){
  'use strict';
  angular.module('s7.browse').controller('BrowseController', BrowseController);

  function BrowseController(ApiService, $state) {
    var vm = this;
    vm.collections = [];
    vm.documents = [];
    vm.filtered = [];
    vm.sortBy = 'updated';
    vm.status = { Published: true, Draft: false, Review: false };

    vm.open = function (id) { $state.go('view', { id: id }); };
    vm.refresh = refresh;
    vm.applyFilters = applyFilters;

    init();

    function init(){
      refresh();
    }
    function refresh(){
      ApiService.collections.list().then(function(c){ vm.collections = c; });
      ApiService.documents.list().then(function(d){ vm.documents = d; applyFilters(); });
    }
    function applyFilters(){
      var allowed = Object.keys(vm.status).filter(k => vm.status[k]);
      vm.filtered = vm.documents.filter(function(x){ return allowed.indexOf(x.status) >= 0; });
    }
  }
})();
