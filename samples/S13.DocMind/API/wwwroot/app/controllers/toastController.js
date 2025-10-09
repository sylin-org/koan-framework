angular.module('s13DocMindApp').controller('ToastController', ['$scope', 'ToastService', function($scope, ToastService) {
    $scope.toasts = ToastService.toasts;

    $scope.removeToast = function(index) {
        if (index >= 0 && index < $scope.toasts.length) {
            ToastService.remove($scope.toasts[index].id);
        }
    };

    $scope.getToastIcon = function(type) {
        return ToastService.getIcon(type);
    };

    // Initialize and show toasts
    $scope.$on('toast:added', function(event, toast) {
        // Trigger Bootstrap toast
        setTimeout(function() {
            var toastElements = document.querySelectorAll('.toast');
            toastElements.forEach(function(element) {
                if (!element.classList.contains('show')) {
                    var bsToast = new bootstrap.Toast(element, {
                        autohide: true,
                        delay: 5000
                    });
                    bsToast.show();
                }
            });
        }, 100);
    });

    $scope.clearAllToasts = function() {
        ToastService.clear();
    };
}]);