angular.module('s13DocMindApp').service('ConfigurationService', ['ApiService', function(ApiService) {
    var service = {
        // Model Management
        getAvailableModels: function() {
            return ApiService.get('/models/available');
        },

        getInstalledModels: function() {
            return ApiService.get('/models/installed');
        },

        searchModels: function(params) {
            return ApiService.get('/models/search', params);
        },

        installModel: function(modelName, provider) {
            var data = provider ? { provider: provider } : {};
            return ApiService.post('/models/' + modelName + '/install', data);
        },

        getModelConfiguration: function() {
            return ApiService.get('/models/config');
        },

        setCurrentTextModel: function(modelName, provider) {
            return ApiService.put('/models/text-model', {
                modelName: modelName,
                provider: provider
            });
        },

        setCurrentVisionModel: function(modelName, provider) {
            return ApiService.put('/models/vision-model', {
                modelName: modelName,
                provider: provider
            });
        },

        analyzeWithModel: function(fileId, typeId, modelOverride) {
            return ApiService.post('/models/analyze', {
                fileId: fileId,
                typeId: typeId,
                modelOverride: modelOverride
            });
        },

        getProviders: function() {
            return ApiService.get('/models/providers');
        },

        checkModelHealth: function() {
            return ApiService.get('/models/health');
        },

        getUsageStats: function() {
            return ApiService.get('/models/usage-stats');
        },

        // Helper methods
        isVisionCapable: function(model) {
            if (!model) return false;
            if (model.isVisionCapable !== undefined) return model.isVisionCapable;

            // Heuristic based on model name
            var name = (model.name || '').toLowerCase();
            return name.includes('vision') ||
                   name.includes('gpt-4-vision') ||
                   name.includes('llava') ||
                   name.includes('minicpm-v') ||
                   name.includes('moondream');
        },

        formatModelSize: function(size) {
            if (!size) return 'Unknown';

            // Handle different size formats
            if (typeof size === 'string') {
                if (size.match(/^\d+[KMGT]?B?$/i)) {
                    return size;
                }
                if (size.match(/^\d+$/)) {
                    return service.formatBytes(parseInt(size));
                }
            }

            return size;
        },

        formatBytes: function(bytes) {
            if (bytes === 0) return '0 B';

            var k = 1024;
            var sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
            var i = Math.floor(Math.log(bytes) / Math.log(k));

            return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
        },

        getProviderIcon: function(provider) {
            var icons = {
                'ollama': 'bi-cpu',
                'openai': 'bi-cloud',
                'azure': 'bi-cloud',
                'anthropic': 'bi-robot'
            };
            return icons[provider.toLowerCase()] || 'bi-gear';
        },

        getModelStatusIcon: function(isInstalled, isActive) {
            if (isActive) return 'bi-check-circle-fill text-success';
            if (isInstalled) return 'bi-download text-info';
            return 'bi-cloud-download text-secondary';
        },

        parseModelTags: function(tags) {
            if (!tags) return [];
            if (Array.isArray(tags)) return tags;
            if (typeof tags === 'string') {
                return tags.split(',').map(function(tag) {
                    return tag.trim();
                }).filter(function(tag) {
                    return tag.length > 0;
                });
            }
            return [];
        },

        categorizeModels: function(models) {
            var categories = {
                text: [],
                vision: [],
                embedding: [],
                other: []
            };

            models.forEach(function(model) {
                var name = (model.name || '').toLowerCase();

                if (service.isVisionCapable(model)) {
                    categories.vision.push(model);
                } else if (name.includes('embedding') || name.includes('ada')) {
                    categories.embedding.push(model);
                } else if (name.includes('gpt') || name.includes('llama') || name.includes('claude')) {
                    categories.text.push(model);
                } else {
                    categories.other.push(model);
                }
            });

            return categories;
        },

        validateModelConfiguration: function(config) {
            var warnings = [];

            if (!config.defaultTextModel) {
                warnings.push('No default text model configured');
            }

            if (!config.defaultVisionModel) {
                warnings.push('No default vision model configured');
            }

            if (!config.availableProviders || config.availableProviders.length === 0) {
                warnings.push('No AI providers available');
            }

            return warnings;
        }
    };

    return service;
}]);