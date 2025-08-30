(function(){
  'use strict';
  angular.module('s7.core')
    .constant('APP_ROUTES', {
      browse: '/browse',
      view: function(id){ return '/view/' + encodeURIComponent(id); },
      editNew: '/edit/new',
      edit: function(id){ return '/edit/' + encodeURIComponent(id); },
      moderate: '/moderate'
    })
    .constant('DOC_STATUS', {
      Draft: 'Draft',
      Review: 'Review',
      Published: 'Published'
    });
})();
