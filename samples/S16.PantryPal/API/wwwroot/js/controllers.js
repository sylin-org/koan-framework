(function(){
  'use strict';

  angular.module('pantryPal')
    .controller('ShellCtrl', ['$scope', '$location', function($scope, $location){
      $scope.isActive = function(path){ return $location.path()===path; };
    }])
    .controller('DashboardCtrl', ['ApiService','ToastService','$scope','$location', function(Api, Toast, $scope, $location){
      var vm=this; vm.loading=true; vm.expiring=[]; vm.meals=[]; vm.stats={}; vm.degraded=false; vm.q='';
      var seven = new Date(Date.now()+7*24*3600*1000).toISOString();
      Api.getData('pantry', { filter: angular.toJson({ ExpiresAt: { $lte: seven }, Status: 'available' }), page:1, pageSize:5, sort:'ExpiresAt' })
        .then(function(r){ var d=r && r.data; vm.expiring=(d && d.items) || d; vm.degraded = (r.headers('X-Search-Degraded')==='1'); })
        .finally(function(){ vm.loading=false; });
      Api.suggestMeals({ maxCookingMinutes:45 }).then(function(r){ var d=r && r.data; vm.meals=(d && d.items) || d; });
      Api.getInsights().then(function(r){ vm.stats=r.data||{}; });
      vm.searchEnter = function($event){ if($event && $event.key==='Enter' && vm.q){ $location.path('/pantry'); $location.search('q', vm.q); } };
    }])
    .controller('PantryCtrl', ['ApiService','ToastService','$scope','$rootScope','$window', function(Api, Toast, $scope, $root, $window){
      var vm=this; vm.items=[]; vm.page=1; vm.pageSize=50; vm.hasMore=true; vm.loading=false; vm.q=''; vm.sort=''; vm.filterJson=''; vm.degraded=false;
      vm.load = function(reset){
        if(reset){ vm.items=[]; vm.page=1; vm.hasMore=true; }
        if(!vm.hasMore || vm.loading) return; vm.loading=true;
        var params={ page:vm.page, pageSize:vm.pageSize, sort:vm.sort, q:vm.q };
        if(vm.filterJson){ params.filter = vm.filterJson; }
        Api.getData('pantry', params).then(function(r){
          var data=r.data; var items = data.items||data; if(items.length<vm.pageSize) vm.hasMore=false; vm.items = vm.items.concat(items); vm.page++; vm.degraded=(r.headers('X-Search-Degraded')==='1');
        }).finally(function(){ vm.loading=false; });
      };
      vm.onSearch = function(){ vm.load(true); };
      vm.toggleExpSoon = function(){ var f = vm.filterJson? JSON.parse(vm.filterJson):{}; var seven = new Date(Date.now()+7*24*3600*1000).toISOString(); if(f.ExpiresAt){ delete f.ExpiresAt; delete f.Status; } else { f.ExpiresAt={ $lte: seven }; f.Status='available'; } vm.filterJson = angular.toJson(f); vm.load(true); };
      vm.sortBy = function(field){ vm.sort = (vm.sort===field?('-'+field):field); vm.load(true); };
      vm.stepQty = function(item, delta){ var old=item.quantity; item.quantity = (item.quantity||0)+delta; Api.patch('pantry', item.id, { quantity: item.quantity }).then(function(){ /* ok */ }, function(){ item.quantity=old; Toast.error('Failed to update quantity'); }); };
      vm.edit = function(item){ $root.$broadcast('openDrawer', item); };
      // initial
      vm.load(true);
    }])
    .controller('CaptureCtrl', ['ApiService','ToastService','$location', function(Api, Toast, $location){
      var vm=this; vm.uploading=false;
  vm.upload = function(file){ if(!file) return; vm.uploading=true; Api.uploadPhoto(file).then(function(r){ var d=r && r.data; var pid = (d && (d.photoId || d.id)); $location.path('/review').search({ photoId: pid }); }, function(){ Toast.error('Upload failed'); }).finally(function(){ vm.uploading=false; }); };
    }])
    .controller('ReviewCtrl', ['$location','$scope', function($location, $scope){
      var vm=this; vm.photoId = $location.search().photoId; vm.detections=[]; vm.selected=null; vm.confirmations={};
      // In a real app, load photo + detections via GET /api/data/photos/{id}?with=*
      vm.select = function(det){ vm.selected=det; };
      vm.chooseCandidate = function(det, cand){ vm.confirmations[det.id] = { detectionId: det.id, selectedCandidateId: cand.id }; };
      vm.openNl = function(det){ $scope.$broadcast('openNlModal', det); };
      vm.toConfirm = function(){ $location.path('/confirm/'+vm.photoId); };
    }])
    .controller('ConfirmCtrl', ['ApiService','$routeParams','ToastService','$location', function(Api, $routeParams, Toast, $location){
      var vm=this; vm.photoId=$routeParams.photoId; vm.pending=[]; vm.submitting=false; vm.results=null;
      vm.submit = function(){ vm.submitting=true; Api.confirmPhoto(vm.photoId, vm.pending).then(function(r){ vm.results=r.data; Toast.success('Pantry updated'); }, function(){ Toast.error('Confirm failed'); }).finally(function(){ vm.submitting=false; }); };
      vm.goPantry = function(){ $location.path('/pantry'); };
      vm.addAnother = function(){ $location.path('/capture'); };
    }])
    .controller('MealsCtrl', ['ApiService','ToastService', function(Api, Toast){
      var vm=this; vm.filters={ maxCookingMinutes:45, dietaryRestrictions:[] }; vm.results=[]; vm.plan={ days:{} };
      vm.suggest = function(){ Api.suggestMeals(vm.filters).then(function(r){ var d=r && r.data; vm.results=(d && d.items) || d; }); };
      vm.addToPlan = function(result, day, slot){
        // Minimal plan: create a plan for the next 7 days with single selection
        var recipe = (result && result.recipe) || result;
        var today = new Date();
        var inTwoDays = new Date(Date.now()+2*24*3600*1000);
        var req = { startDate: today.toISOString(), endDate: new Date(Date.now()+7*24*3600*1000).toISOString(), meals: [ { recipeId: recipe.id || recipe.Id || '', recipeName: recipe.name || (recipe.recipe && recipe.recipe.name) || '', scheduledFor: inTwoDays.toISOString(), mealType: 'dinner', servings: 2 } ] };
        Api.planMeals(req).then(function(r){ var d=r && r.data; Toast.success('Added to plan'); }, function(err){ Toast.error('Failed to add to plan'); });
      };
    }])
    .controller('ShoppingCtrl', ['ApiService', function(Api){ var vm=this; vm.groups=[]; /* derive from plan or ask server */ }])
    .controller('InsightsCtrl', ['ApiService', function(Api){ var vm=this; vm.stats=null; Api.getInsights().then(function(r){ vm.stats=r.data; }); }])
    .controller('BehindCtrl', ['RecentRequestsService','$location', function(Reqs, $location){
      var vm=this; vm.calls = Reqs.list(); vm.params = $location.search();
    }]);
})();
