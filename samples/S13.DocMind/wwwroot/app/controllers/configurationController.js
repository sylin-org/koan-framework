angular.module('s13DocMindApp').controller('ConfigurationController', [
    '$scope', '$location', 'ConfigurationService', 'ToastService',
    function($scope, $location, ConfigurationService, ToastService) {

        $scope.loading = true;
        $scope.saving = false;
        $scope.testing = {};
        $scope.activeTab = 'models';

        // Configuration sections
        $scope.config = {
            models: {
                openai: {
                    apiKey: '',
                    baseUrl: 'https://api.openai.com/v1',
                    defaultModel: 'gpt-4-turbo',
                    maxTokens: 4096,
                    temperature: 0.3,
                    enabled: true
                },
                ollama: {
                    baseUrl: 'http://localhost:11434',
                    defaultModel: 'llama3.1:8b',
                    enabled: false,
                    availableModels: []
                },
                anthropic: {
                    apiKey: '',
                    baseUrl: 'https://api.anthropic.com',
                    defaultModel: 'claude-3-5-sonnet-20241022',
                    maxTokens: 4096,
                    temperature: 0.3,
                    enabled: false
                }
            },
            storage: {
                mongodb: {
                    connectionString: 'mongodb://localhost:27017/s13docmind',
                    database: 's13docmind',
                    enabled: true
                },
                weaviate: {
                    endpoint: 'http://localhost:8080',
                    apiKey: '',
                    className: 'Document',
                    enabled: true
                },
                redis: {
                    connectionString: 'localhost:6379',
                    database: 0,
                    enabled: false
                }
            },
            processing: {
                maxConcurrentProcessing: 3,
                analysisTimeout: 300,
                retryAttempts: 2,
                tempFileRetentionHours: 24,
                maxFileSize: 52428800,
                enableBackgroundProcessing: true
            },
            security: {
                enableAuthentication: false,
                jwtSecret: '',
                sessionTimeout: 3600,
                allowedOrigins: ['http://localhost:8080'],
                enableCors: true
            }
        };

        $scope.tabs = [
            { id: 'models', label: 'AI Models', icon: 'bi-brain' },
            { id: 'storage', label: 'Data Storage', icon: 'bi-database' },
            { id: 'processing', label: 'Processing', icon: 'bi-gear' },
            { id: 'security', label: 'Security', icon: 'bi-shield-lock' }
        ];

        function initialize() {
            $scope.loading = true;
            loadConfiguration()
                .then(function() {
                    if ($scope.config.models.ollama.enabled) {
                        return loadOllamaModels();
                    }
                })
                .then(function() {
                    $scope.loading = false;
                    $scope.$apply();
                })
                .catch(function(error) {
                    console.error('Failed to load configuration:', error);
                    ToastService.error('Failed to load configuration');
                    $scope.loading = false;
                    $scope.$apply();
                });
        }

        function loadConfiguration() {
            return ConfigurationService.getConfiguration()
                .then(function(config) {
                    if (config) {
                        $scope.config = angular.extend(true, $scope.config, config);
                    }
                    return config;
                })
                .catch(function(error) {
                    console.error('Failed to load configuration:', error);
                    throw error;
                });
        }

        function loadOllamaModels() {
            return ConfigurationService.getOllamaModels()
                .then(function(models) {
                    $scope.config.models.ollama.availableModels = models || [];
                    return models;
                })
                .catch(function(error) {
                    console.warn('Failed to load Ollama models:', error);
                    $scope.config.models.ollama.availableModels = [];
                    return [];
                });
        }

        // Tab management
        $scope.setActiveTab = function(tabId) {
            $scope.activeTab = tabId;
        };

        $scope.isActiveTab = function(tabId) {
            return $scope.activeTab === tabId;
        };

        // Configuration management
        $scope.saveConfiguration = function() {
            if ($scope.saving) return;

            $scope.saving = true;

            ConfigurationService.updateConfiguration($scope.config)
                .then(function() {
                    ToastService.success('Configuration saved successfully');
                    $scope.saving = false;
                    $scope.$apply();
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to save configuration');
                    $scope.saving = false;
                    $scope.$apply();
                });
        };

        $scope.resetConfiguration = function() {
            if (!confirm('Are you sure you want to reset all configuration to default values? This action cannot be undone.')) {
                return;
            }

            ConfigurationService.resetConfiguration()
                .then(function() {
                    ToastService.success('Configuration reset to defaults');
                    return loadConfiguration();
                })
                .then(function() {
                    $scope.$apply();
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to reset configuration');
                });
        };

        // Model testing
        $scope.testModel = function(provider) {
            if ($scope.testing[provider]) return;

            $scope.testing[provider] = true;

            var config = $scope.config.models[provider];
            ConfigurationService.testModel(provider, config)
                .then(function(result) {
                    if (result.success) {
                        ToastService.success('Model connection successful');
                    } else {
                        ToastService.error('Model test failed: ' + result.error);
                    }
                    $scope.testing[provider] = false;
                    $scope.$apply();
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to test model');
                    $scope.testing[provider] = false;
                    $scope.$apply();
                });
        };

        $scope.refreshOllamaModels = function() {
            if ($scope.testing.ollama) return;

            $scope.testing.ollama = true;
            loadOllamaModels()
                .then(function() {
                    ToastService.success('Ollama models refreshed');
                    $scope.testing.ollama = false;
                    $scope.$apply();
                })
                .catch(function(error) {
                    ToastService.error('Failed to refresh Ollama models');
                    $scope.testing.ollama = false;
                    $scope.$apply();
                });
        };

        // Storage testing
        $scope.testStorage = function(provider) {
            if ($scope.testing[provider]) return;

            $scope.testing[provider] = true;

            var config = $scope.config.storage[provider];
            ConfigurationService.testStorage(provider, config)
                .then(function(result) {
                    if (result.success) {
                        ToastService.success('Storage connection successful');
                    } else {
                        ToastService.error('Storage test failed: ' + result.error);
                    }
                    $scope.testing[provider] = false;
                    $scope.$apply();
                })
                .catch(function(error) {
                    ToastService.handleError(error, 'Failed to test storage');
                    $scope.testing[provider] = false;
                    $scope.$apply();
                });
        };

        // Validation helpers
        $scope.isValidUrl = function(url) {
            try {
                new URL(url);
                return true;
            } catch (e) {
                return false;
            }
        };

        $scope.isValidConnectionString = function(connectionString) {
            return connectionString && connectionString.length > 0;
        };

        // Helper methods
        $scope.formatBytes = function(bytes) {
            if (bytes === 0) return '0 B';
            var k = 1024;
            var sizes = ['B', 'KB', 'MB', 'GB'];
            var i = Math.floor(Math.log(bytes) / Math.log(k));
            return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
        };

        $scope.getProviderStatusClass = function(provider, section) {
            var config = $scope.config[section][provider];
            if (!config.enabled) return 'text-secondary';

            // Basic validation
            if (section === 'models') {
                if (provider === 'openai' || provider === 'anthropic') {
                    return config.apiKey ? 'text-success' : 'text-warning';
                }
                if (provider === 'ollama') {
                    return config.baseUrl && $scope.isValidUrl(config.baseUrl) ? 'text-success' : 'text-warning';
                }
            }

            if (section === 'storage') {
                if (provider === 'mongodb' || provider === 'redis') {
                    return config.connectionString ? 'text-success' : 'text-warning';
                }
                if (provider === 'weaviate') {
                    return config.endpoint && $scope.isValidUrl(config.endpoint) ? 'text-success' : 'text-warning';
                }
            }

            return 'text-success';
        };

        $scope.getProviderStatusText = function(provider, section) {
            var config = $scope.config[section][provider];
            if (!config.enabled) return 'Disabled';

            var statusClass = $scope.getProviderStatusClass(provider, section);
            switch (statusClass) {
                case 'text-success': return 'Ready';
                case 'text-warning': return 'Configuration Required';
                case 'text-danger': return 'Error';
                default: return 'Unknown';
            }
        };

        $scope.generateJwtSecret = function() {
            var chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
            var secret = '';
            for (var i = 0; i < 64; i++) {
                secret += chars.charAt(Math.floor(Math.random() * chars.length));
            }
            $scope.config.security.jwtSecret = secret;
        };

        $scope.addAllowedOrigin = function() {
            var origin = prompt('Enter allowed origin (e.g., https://example.com):');
            if (origin && origin.trim()) {
                if (!$scope.config.security.allowedOrigins.includes(origin.trim())) {
                    $scope.config.security.allowedOrigins.push(origin.trim());
                }
            }
        };

        $scope.removeAllowedOrigin = function(index) {
            $scope.config.security.allowedOrigins.splice(index, 1);
        };

        // Initialize
        initialize();
    }
]);