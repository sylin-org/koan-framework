angular.module('s13DocMindApp').service('ToastService', ['$rootScope', '$timeout', function($rootScope, $timeout) {
    var toasts = [];

    var service = {
        toasts: toasts,

        show: function(message, type, duration) {
            type = type || 'info';
            duration = duration || 5000;

            var toast = {
                id: Date.now() + Math.random(),
                message: message,
                type: type,
                timestamp: new Date()
            };

            toasts.push(toast);

            // Auto-remove toast after duration
            if (duration > 0) {
                $timeout(function() {
                    service.remove(toast.id);
                }, duration);
            }

            // Broadcast to controllers
            $rootScope.$broadcast('toast:added', toast);

            return toast.id;
        },

        success: function(message, duration) {
            return service.show(message, 'success', duration);
        },

        info: function(message, duration) {
            return service.show(message, 'info', duration);
        },

        warning: function(message, duration) {
            return service.show(message, 'warning', duration);
        },

        error: function(message, duration) {
            return service.show(message, 'danger', duration || 8000); // Longer duration for errors
        },

        remove: function(id) {
            for (var i = toasts.length - 1; i >= 0; i--) {
                if (toasts[i].id === id) {
                    toasts.splice(i, 1);
                    break;
                }
            }
        },

        clear: function() {
            toasts.length = 0;
        },

        // Helper methods
        getIcon: function(type) {
            var icons = {
                'success': 'bi-check-circle',
                'info': 'bi-info-circle',
                'warning': 'bi-exclamation-triangle',
                'danger': 'bi-x-circle'
            };
            return icons[type] || 'bi-info-circle';
        },

        // Handle API errors
        handleError: function(error, defaultMessage) {
            var message = defaultMessage || 'An error occurred';

            if (error && error.message) {
                message = error.message;
            } else if (error && error.data && error.data.message) {
                message = error.data.message;
            } else if (typeof error === 'string') {
                message = error;
            }

            service.error(message);
        },

        // Handle API success responses
        handleSuccess: function(message) {
            service.success(message);
        },

        // Show loading indicator
        showLoading: function(message) {
            return service.show(message || 'Loading...', 'info', 0); // 0 duration = manual removal
        },

        // Process operation with loading
        withLoading: function(promise, loadingMessage, successMessage) {
            var loadingId = service.showLoading(loadingMessage || 'Processing...');

            return promise.then(function(result) {
                service.remove(loadingId);
                if (successMessage) {
                    service.success(successMessage);
                }
                return result;
            }).catch(function(error) {
                service.remove(loadingId);
                service.handleError(error);
                throw error;
            });
        }
    };

    return service;
}]);