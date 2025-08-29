(function(){
  'use strict';
  angular.module('s7.moderate').controller('ModerateController', function(ApiService){
    var vm = this;
    vm.queue = [];
    ApiService.moderation.queue().then(function(q){ vm.queue = q.items || q; });
  });
})();
