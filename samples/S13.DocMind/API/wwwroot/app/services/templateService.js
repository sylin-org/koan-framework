angular.module('s13DocMindApp').service('TemplateService', ['ApiService', function(ApiService) {
    var service = {
        // EntityController<SemanticTypeProfile> provides these automatically:
        getAll: function() {
            return ApiService.get('/document-types');
        },

        getById: function(id) {
            return ApiService.get('/document-types/' + id);
        },

        create: function(template) {
            return ApiService.post('/document-types', template);
        },

        update: function(id, template) {
            return ApiService.put('/document-types/' + id, template);
        },

        delete: function(id) {
            return ApiService.delete('/document-types/' + id);
        },

        // Business-specific endpoints from TemplatesController:
        generate: function(request) {
            return ApiService.post('/document-types/generate', request);
        },

        promptTest: function(id, request) {
            return ApiService.post('/document-types/' + id + '/prompt-test', request);
        },

        // Helper methods for UI display
        getTypeIcon: function(template) {
            if (!template || !template.category) return 'bi-file-text';

            var category = template.category.toLowerCase();
            var icons = {
                'meeting': 'bi-people',
                'technical': 'bi-code-square',
                'feature': 'bi-lightbulb',
                'financial': 'bi-receipt',
                'legal': 'bi-file-contract',
                'report': 'bi-graph-up',
                'analysis': 'bi-bar-chart'
            };

            return icons[category] || 'bi-file-text';
        },

        validateTemplate: function(template) {
            var errors = [];

            if (!template.name || template.name.trim().length === 0) {
                errors.push('Name is required');
            }

            if (!template.description || template.description.trim().length === 0) {
                errors.push('Description is required');
            }

            if (template.prompt) {
                if (!template.prompt.systemPrompt || template.prompt.systemPrompt.trim().length === 0) {
                    errors.push('System prompt is required');
                }

                if (!template.prompt.userTemplate || template.prompt.userTemplate.trim().length === 0) {
                    errors.push('User template is required');
                }
            }

            return errors;
        },

        getDefaultSystemPrompt: function() {
            return 'You are a document analysis expert. Extract key information from the provided document and structure it according to the template provided.';
        },

        getDefaultUserTemplate: function(category) {
            var templates = {
                'meeting': 'Extract the following from this meeting document:\n- Date and attendees\n- Key discussion points\n- Decisions made\n- Action items with owners',
                'technical': 'Extract the following from this technical document:\n- Overview and purpose\n- Technical requirements\n- Architecture details\n- Implementation notes',
                'financial': 'Extract the following from this financial document:\n- Document type and number\n- Parties involved\n- Financial amounts\n- Key terms and dates',
                'legal': 'Extract the following from this legal document:\n- Document type\n- Parties involved\n- Key terms and obligations\n- Important dates'
            };

            return templates[category] || 'Extract key information from this document in a structured format.';
        }
    };

    return service;
}]);