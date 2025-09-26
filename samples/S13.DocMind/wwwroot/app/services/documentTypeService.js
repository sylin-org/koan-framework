angular.module('s13DocMindApp').service('DocumentTypeService', ['ApiService', function(ApiService) {
    var service = {
        getAllTypes: function() {
            return ApiService.get('/document-types');
        },

        getType: function(id) {
            return ApiService.get('/document-types/' + id);
        },

        createType: function(type) {
            return ApiService.post('/document-types', type);
        },

        updateType: function(id, type) {
            return ApiService.put('/document-types/' + id, type);
        },

        deleteType: function(id) {
            return ApiService.delete('/document-types/' + id);
        },

        generateType: function(prompt) {
            return ApiService.post('/document-types/generate', {
                prompt: prompt
            });
        },

        getTypeFiles: function(id) {
            return ApiService.get('/document-types/' + id + '/files');
        },

        getTypeAnalyses: function(id) {
            return ApiService.get('/document-types/' + id + '/analyses');
        },

        getTypeStats: function(id) {
            return ApiService.get('/document-types/' + id + '/stats');
        },

        searchTypes: function(query, tags, limit) {
            var params = {};
            if (query) params.query = query;
            if (tags && tags.length) params.tags = tags;
            if (limit) params.limit = limit;

            return ApiService.get('/document-types/search', params);
        },

        analyzeFileWithType: function(typeId, fileId) {
            return ApiService.post('/document-types/' + typeId + '/analyze-file', {
                fileId: fileId
            });
        },

        initializeDefaultTypes: function() {
            return ApiService.post('/document-types/initialize-defaults');
        },

        // Helper methods
        getTypeIcon: function(code) {
            var icons = {
                'MEETING': 'bi-people',
                'TECH_SPEC': 'bi-code-square',
                'FEATURE': 'bi-lightbulb',
                'INVOICE': 'bi-receipt',
                'CONTRACT': 'bi-file-contract',
                'REPORT': 'bi-graph-up'
            };
            return icons[code] || 'bi-file-text';
        },

        validateType: function(type) {
            var errors = [];

            if (!type.name || type.name.trim().length === 0) {
                errors.push('Name is required');
            }

            if (!type.code || type.code.trim().length === 0) {
                errors.push('Code is required');
            }

            if (type.code && !/^[A-Z0-9_]+$/.test(type.code)) {
                errors.push('Code must contain only uppercase letters, numbers, and underscores');
            }

            if (!type.extractionPrompt || type.extractionPrompt.trim().length === 0) {
                errors.push('Extraction prompt is required');
            }

            if (!type.templateStructure || type.templateStructure.trim().length === 0) {
                errors.push('Template structure is required');
            }

            return errors;
        },

        generateSampleTemplate: function(typeName) {
            var templates = {
                'Meeting Notes': '## Meeting Summary\n**Date:** {{date}}\n**Attendees:** {{attendees}}\n\n### Key Points:\n{{key_points}}\n\n### Decisions:\n{{decisions}}\n\n### Action Items:\n{{action_items}}',
                'Technical Specification': '# Technical Specification\n\n## Overview\n{{overview}}\n\n## Requirements\n{{requirements}}\n\n## Architecture\n{{architecture}}\n\n## Implementation\n{{implementation}}',
                'Feature Request': '# Feature Request\n\n## Description\n{{description}}\n\n## Business Value\n{{business_value}}\n\n## Requirements\n{{requirements}}\n\n## Acceptance Criteria\n{{acceptance_criteria}}',
                'Invoice': '# Invoice Details\n\n**Invoice Number:** {{invoice_number}}\n**Date:** {{date}}\n**Vendor:** {{vendor}}\n**Total Amount:** {{total_amount}}\n\n## Line Items:\n{{line_items}}',
                'Contract': '# Contract Summary\n\n**Parties:** {{parties}}\n**Effective Date:** {{effective_date}}\n**Expiration Date:** {{expiration_date}}\n**Key Terms:** {{key_terms}}\n**Obligations:** {{obligations}}'
            };

            return templates[typeName] || '# {{document_type}}\n\n## Summary\n{{summary}}\n\n## Key Information\n{{key_information}}';
        }
    };

    return service;
}]);