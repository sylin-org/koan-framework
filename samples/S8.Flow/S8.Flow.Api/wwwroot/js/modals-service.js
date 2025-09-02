(function() {
    'use strict';

    angular.module('flowDashboard')
        .factory('modalsService', modalsService);

    modalsService.$inject = ['$rootScope', '$compile', 'apiService'];

    function modalsService($rootScope, $compile, apiService) {
        var service = {
            openSeedModal: openSeedModal,
            openBulkImportModal: openBulkImportModal
        };

        return service;

        function openSeedModal() {
            var modalContent = `
                <div class="fixed inset-0 bg-slate-900 bg-opacity-75 flex items-center justify-center z-50">
                    <div class="bg-slate-800 rounded-lg shadow-xl p-8 w-full max-w-md">
                        <h3 class="text-lg font-bold mb-4">Seed Adapters</h3>
                        <p class="text-slate-400 mb-6">How many devices should each adapter generate?</p>
                        <input type="number" id="seedCount" class="bg-slate-700 border border-slate-600 rounded-md w-full p-2 mb-6" value="1000">
                        <div class="flex justify-end space-x-4">
                            <button onclick="closeModal()" class="btn-secondary">Cancel</button>
                            <button id="confirmSeed" class="btn-primary">Seed</button>
                        </div>
                    </div>
                </div>
            `;
            var modalElement = angular.element(modalContent);
            var scope = $rootScope.$new(true);
            var compiledModal = $compile(modalElement)(scope);
            angular.element(document.getElementById('modalContainer')).append(compiledModal);

            document.getElementById('confirmSeed').onclick = function() {
                var count = parseInt(document.getElementById('seedCount').value, 10);
                apiService.seedAdapters(count).then(function() {
                    $rootScope.$broadcast('new-activity', { message: 'Adapter seeding initiated for ' + count + ' devices.', timestamp: new Date() });
                    closeModal();
                });
            };
        }

        function openBulkImportModal() {
            var modalContent = `
                <div class="fixed inset-0 bg-slate-900 bg-opacity-75 flex items-center justify-center z-50">
                    <div class="bg-slate-800 rounded-lg shadow-xl p-8 w-full max-w-lg">
                        <h3 class="text-lg font-bold mb-4">Bulk Import Data</h3>
                        <textarea id="bulkData" class="bg-slate-700 border border-slate-600 rounded-md w-full p-2 h-64 mb-6" placeholder="Paste JSON array of events here..."></textarea>
                        <div class="flex justify-end space-x-4">
                            <button onclick="closeModal()" class="btn-secondary">Cancel</button>
                            <button id="confirmImport" class="btn-primary">Import</button>
                        </div>
                    </div>
                </div>
            `;
            var modalElement = angular.element(modalContent);
            var scope = $rootScope.$new(true);
            var compiledModal = $compile(modalElement)(scope);
            angular.element(document.getElementById('modalContainer')).append(compiledModal);

            document.getElementById('confirmImport').onclick = function() {
                var data = document.getElementById('bulkData').value;
                try {
                    var jsonData = JSON.parse(data);
                    apiService.bulkImport(jsonData).then(function() {
                        $rootScope.$broadcast('new-activity', { message: 'Bulk import successful.', timestamp: new Date() });
                        closeModal();
                    });
                } catch (e) {
                    alert('Invalid JSON data.');
                }
            };
        }

        window.closeModal = function() {
            angular.element(document.getElementById('modalContainer')).empty();
        };
    }
})();
