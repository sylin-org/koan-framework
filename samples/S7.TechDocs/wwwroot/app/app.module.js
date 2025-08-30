(function () {
  'use strict';

  angular.module('s7.core', []);
  angular.module('s7.shared', []);
  angular.module('s7.browse', []);
  angular.module('s7.view', []);
  angular.module('s7.edit', []);
  angular.module('s7.moderate', []);
  angular.module('s7.admin', []);

  angular.module('s7.app', [
  'ui.router',
  'ngSanitize',
    's7.core', 's7.shared',
    's7.browse', 's7.view', 's7.edit', 's7.moderate', 's7.admin'
  ]);
})();
