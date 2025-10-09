(function(){
  'use strict';

  angular.module('pantryPal')
    .directive('infiniteScroll', ['$window', function($window){
      return { restrict:'A', scope:{ infiniteScroll:'&', busy:'<' }, link:function(s,el){
        function onScroll(){ if(s.busy) return; var rect = el[0].getBoundingClientRect(); if(rect.bottom - window.innerHeight < 200){ s.$applyAsync(function(){ s.infiniteScroll(); }); } }
        window.addEventListener('scroll', onScroll); s.$on('$destroy', function(){ window.removeEventListener('scroll', onScroll); });
      }};
    }])
    // file-select: Use on <input type="file" file-select="vm.upload(files[0])">
    // Avoids ng-change's ngModel requirement and provides the FileList
    .directive('fileSelect', [function(){
      return { restrict:'A', scope:{ fileSelect:'&' }, link:function(s, el){
        function onChange(evt){ var files = (evt.target && evt.target.files) || []; s.$applyAsync(function(){ s.fileSelect({ $event: evt, files: files }); }); }
        el[0].addEventListener('change', onChange);
        s.$on('$destroy', function(){ el[0].removeEventListener('change', onChange); });
      }};
    }])
    .directive('toastContainer', [function(){
      return { restrict:'E', template:
        '<div class="fixed top-3 right-3 space-y-2 z-50">'+
        '  <div ng-repeat="t in toasts" class="px-3 py-2 rounded-xl shadow-sm ring-1" ng-class="{\'bg-emerald-50 text-emerald-800 ring-emerald-200\':t.type===\'success\', \'bg-blue-50 text-blue-800 ring-blue-200\':t.type===\'info\', \'bg-amber-50 text-amber-800 ring-amber-200\':t.type===\'warn\', \'bg-rose-50 text-rose-800 ring-rose-200\':t.type===\'error\'}">{{t.text}}</div>'+
        '</div>'
      };
    }])
    .directive('degradedChip', [function(){
      return { restrict:'E', scope:{ show:'<' }, template: '<span ng-if="show" class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 bg-yellow-50 text-yellow-800 ring-yellow-200">Semantic offline — using lexical</span>' };
    }])
    .directive('qtyStepper', ['ApiService','ToastService', function(Api, Toast){
      return { restrict:'E', scope:{ item:'=' }, template:
        '<div class="inline-flex items-center gap-2" role="group" aria-label="Quantity controls">'+
        ' <button class="h-8 px-2 bg-white rounded-lg ring-1 ring-gray-200 hover:bg-gray-50" ng-click="dec()" aria-label="Decrease">−</button>'+
        ' <span class="min-w-8 text-center text-sm font-medium">{{item.quantity||0}}</span>'+
        ' <button class="h-8 px-2 bg-white rounded-lg ring-1 ring-gray-200 hover:bg-gray-50" ng-click="inc()" aria-label="Increase">+</button>'+
        '</div>',
        link:function(s){
          function applyPatch(oldVal){
            return Api.patch('pantry', s.item.id, { quantity: s.item.quantity }).catch(function(err){
              // Fallback: GET latest then PUT full entity with updated quantity
              return Api.getById('pantry', s.item.id).then(function(r){
                var entity = r && r.data || {};
                entity.quantity = s.item.quantity;
                return Api.put('pantry', s.item.id, entity);
              });
            });
          }
          s.inc=function(){ var old=s.item.quantity||0; s.item.quantity=old+1; applyPatch(old).catch(function(err){ s.item.quantity=old; Toast.error((err && err.data && (err.data.message||err.data.error)) || 'Failed to update'); }); };
          s.dec=function(){ var old=s.item.quantity||0; s.item.quantity=Math.max(0, old-1); applyPatch(old).catch(function(err){ s.item.quantity=old; Toast.error((err && err.data && (err.data.message||err.data.error)) || 'Failed to update'); }); };
        }
      };
    }])
    .directive('capOverlayPill', [function(){
      return { restrict:'E', scope:{ text:'@', detail:'@' }, template:
        '<span class="pointer-events-none select-none absolute bottom-2 right-2 text-[10px] rounded-full bg-gray-800/70 text-white px-2 py-0.5" title="{{detail}}">{{text}}</span>' };
    }]);
})();
