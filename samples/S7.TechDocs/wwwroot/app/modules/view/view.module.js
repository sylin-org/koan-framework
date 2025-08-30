(function(){
  'use strict';
  angular.module('s7.view').controller('ViewController', ViewController);

  function ViewController($stateParams, ApiService) {
    var vm = this;
    vm.doc = null;

    init();

    function init(){
      var id = $stateParams.id;
      ApiService.documents.get(id).then(function(d){ vm.doc = d; ApiService.documents.trackView(id); });
    }
  }
})();
