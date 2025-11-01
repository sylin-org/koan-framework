/**
 * PolyglotShop - Constants
 * API endpoints and UI constants
 */
(function() {
    'use strict';

    const constants = {
        // API Endpoints
        endpoints: {
            translate: '/api/translation/translate',
            detect: '/api/translation/detect',
            languages: '/api/translation/languages'
        },

        // UI Constants
        ui: {
            toastDuration: 4000,
            debounceDelay: 300
        }
    };

    window.S8Const = constants;
})();
