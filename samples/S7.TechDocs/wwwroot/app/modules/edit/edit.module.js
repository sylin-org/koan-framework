(function(){
  'use strict';
  angular.module('s7.edit').controller('EditController', EditController);

  function EditController(doc, ApiService, $state, APP_ROUTES) {
    var vm = this;
    vm.doc = angular.copy(doc || { title:'', summary:'', content:'', collectionId:'', tags:[] });
    vm.saving = false;

    vm.save = save;

    function save(){
      vm.saving = true;
      var p = vm.doc.id ? ApiService.documents.update(vm.doc.id, vm.doc)
                        : ApiService.documents.create(vm.doc);
      p.then(function(saved){
        vm.saving = false;
        $state.go('view', { id: saved.id });
      }).catch(function(){ vm.saving = false; });
    }
  }
})();
