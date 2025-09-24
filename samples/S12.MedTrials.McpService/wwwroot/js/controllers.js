(function () {
    'use strict';

    angular.module('MedTrialsMcpApp')
        .controller('ShellController', ['$scope', 'mcpClient', function ($scope, mcpClient) {
            $scope.status = mcpClient.status;

            $scope.statusBadgeClass = function (state) {
                switch ((state || '').toLowerCase()) {
                    case 'connected':
                        return 'status-connected';
                    case 'connecting':
                        return 'status-connecting';
                    default:
                        return 'status-disconnected';
                }
            };

            $scope.formatTimestamp = function (value) {
                if (!value) {
                    return '—';
                }
                return new Date(value).toLocaleString();
            };
        }])
        .controller('OverviewController', ['$scope', 'mcpClient', function ($scope, mcpClient) {
            $scope.loading = true;
            $scope.capabilities = null;
            $scope.transports = [];
            $scope.tools = [];
            $scope.error = null;

            function load() {
                $scope.loading = true;
                mcpClient.getCapabilities()
                    .then(function (document) {
                        $scope.capabilities = document;
                        $scope.transports = document.transports || [];
                        $scope.tools = document.tools || [];
                    })
                    .catch(function (error) {
                        console.error('Failed to load MCP capabilities', error);
                        $scope.error = error;
                        var message = (error && error.message) || 'Failed to load MCP capability document.';
                        $scope.$root.showAlert('danger', message);
                    })
                    .finally(function () {
                        $scope.loading = false;
                    });
            }

            $scope.refresh = function () {
                $scope.loading = true;
                mcpClient.refreshCapabilities()
                    .then(function (document) {
                        $scope.capabilities = document;
                        $scope.transports = document.transports || [];
                        $scope.tools = document.tools || [];
                        $scope.$root.showAlert('success', 'Capability document refreshed.');
                    })
                    .catch(function (error) {
                        console.error('Failed to refresh MCP capabilities', error);
                        var message = (error && error.message) || 'Capability refresh failed.';
                        $scope.$root.showAlert('danger', message);
                    })
                    .finally(function () {
                        $scope.loading = false;
                    });
            };

            $scope.toolCount = function () {
                return ($scope.tools || []).length;
            };

            $scope.securedToolCount = function () {
                return ($scope.tools || []).filter(function (tool) { return tool.requireAuthentication; }).length;
            };

            $scope.primaryTransport = function () {
                return ($scope.transports || [])[0] || null;
            };

            load();
        }])
        .controller('ToolsController', ['$scope', 'mcpClient', function ($scope, mcpClient) {
            $scope.loading = true;
            $scope.tools = [];
            $scope.filterText = '';
            $scope.expanded = {};

            function loadTools(force) {
                $scope.loading = true;
                var loader = force ? mcpClient.refreshTools() : mcpClient.listTools();
                loader.then(function (tools) {
                    $scope.tools = tools || [];
                }).catch(function (error) {
                    console.error('Failed to load MCP tools', error);
                    var message = (error && error.message) || 'Unable to load MCP tools.';
                    $scope.$root.showAlert('danger', message);
                }).finally(function () {
                    $scope.loading = false;
                });
            }

            $scope.toggleSchema = function (tool) {
                if (!tool || !tool.name) {
                    return;
                }
                $scope.expanded[tool.name] = !$scope.expanded[tool.name];
            };

            $scope.isSchemaVisible = function (tool) {
                return tool && tool.name ? !!$scope.expanded[tool.name] : false;
            };

            $scope.filterPredicate = function (tool) {
                if (!$scope.filterText) {
                    return true;
                }
                var text = $scope.filterText.toLowerCase();
                var nameMatch = tool.name && tool.name.toLowerCase().indexOf(text) !== -1;
                var descriptionMatch = tool.description && tool.description.toLowerCase().indexOf(text) !== -1;
                var entityMatch = tool.entity && tool.entity.toLowerCase().indexOf(text) !== -1;
                return nameMatch || descriptionMatch || entityMatch;
            };

            $scope.refresh = function () {
                loadTools(true);
                $scope.$root.showAlert('info', 'Refreshing MCP tool catalogue…');
            };

            loadTools(false);
        }])
        .controller('ExplorerController', ['$scope', 'mcpClient', function ($scope, mcpClient) {
            $scope.loading = false;
            $scope.tools = [];
            $scope.selectedTool = null;
            $scope.argumentsJson = '{\n  \n}';
            $scope.result = null;
            $scope.resultJson = '';
            $scope.headersJson = '';
            $scope.diagnosticsJson = '';
            $scope.warnings = [];
            $scope.error = null;

            function selectFirstTool(tools) {
                if (!Array.isArray(tools) || tools.length === 0) {
                    $scope.selectedTool = null;
                    return;
                }
                $scope.selectedTool = tools[0];
                $scope.argumentsJson = '{\n  \n}';
            }

            function loadTools() {
                $scope.loading = true;
                mcpClient.listTools()
                    .then(function (tools) {
                        $scope.tools = tools || [];
                        if (!$scope.selectedTool && $scope.tools.length > 0) {
                            selectFirstTool($scope.tools);
                        }
                    })
                    .catch(function (error) {
                        console.error('Failed to load tools for explorer', error);
                        var message = (error && error.message) || 'Unable to load tools for explorer.';
                        $scope.$root.showAlert('danger', message);
                    })
                    .finally(function () {
                        $scope.loading = false;
                    });
            }

            $scope.selectTool = function (toolName) {
                if (!toolName) {
                    $scope.selectedTool = null;
                    return;
                }
                var match = ($scope.tools || []).find(function (tool) { return tool.name === toolName; });
                $scope.selectedTool = match || null;
                $scope.argumentsJson = '{\n  \n}';
                $scope.result = null;
                $scope.resultJson = '';
                $scope.headersJson = '';
                $scope.diagnosticsJson = '';
                $scope.warnings = [];
                $scope.error = null;
            };

            function parseArguments(text) {
                if (!text) {
                    return {};
                }
                var trimmed = text.trim();
                if (!trimmed) {
                    return {};
                }
                try {
                    return JSON.parse(trimmed);
                } catch (err) {
                    throw new Error('Payload must be valid JSON.');
                }
            }

            $scope.invoke = function () {
                if (!$scope.selectedTool) {
                    $scope.$root.showAlert('warning', 'Select a tool before invoking.');
                    return;
                }

                var args;
                try {
                    args = parseArguments($scope.argumentsJson);
                } catch (err) {
                    $scope.$root.showAlert('danger', err.message || 'Invalid payload.');
                    return;
                }

                $scope.loading = true;
                $scope.result = null;
                $scope.resultJson = '';
                $scope.headersJson = '';
                $scope.diagnosticsJson = '';
                $scope.warnings = [];
                $scope.error = null;

                mcpClient.callTool($scope.selectedTool.name, args)
                    .then(function (response) {
                        $scope.result = response;
                        $scope.resultJson = response && response.result ? JSON.stringify(response.result, null, 2) : '';
                        $scope.headersJson = response && response.headers ? JSON.stringify(response.headers, null, 2) : '{}';
                        $scope.diagnosticsJson = response && response.diagnostics ? JSON.stringify(response.diagnostics, null, 2) : '{}';
                        $scope.warnings = response && Array.isArray(response.warnings) ? response.warnings : [];
                        if (response && response.success) {
                            $scope.$root.showAlert('success', 'Tool executed successfully.');
                        } else {
                            var warn = (response && response.errorMessage) ? response.errorMessage : 'Tool returned diagnostics. Review the response details.';
                            $scope.$root.showAlert('warning', warn);
                        }
                    })
                    .catch(function (error) {
                        $scope.error = error;
                        var message = (error && error.message) || 'Tool execution failed.';
                        $scope.$root.showAlert('danger', message);
                    })
                    .finally(function () {
                        $scope.loading = false;
                    });
            };

            $scope.resetPayload = function () {
                $scope.argumentsJson = '{\n  \n}';
            };

            loadTools();
        }]);
})();
