# Meridian AI Authoring Enhancement Plan

**Date**: 2025-06-01  
**Status**: Proposed  
**Scope**: Enhance AI-powered analysis type generation to produce rich, contextual templates

## 1. Executive Summary

The current AI authoring system in Meridian generates basic analysis types with 3-7 fields and simple templates. This enhancement enables generation of **rich, contextual analysis types** with:

- 20+ fields with metadata (descriptions, types, validation)
- Multi-section markdown templates
- Role-based processing instructions
- Detailed extraction guidance

**Target Example**: Enterprise Architecture Review with comprehensive review sections, strategic recommendations, and contextual AI prompts.

## 2. Current State

### Architecture

```
User Input → AnalysisTypeAuthoringService → LLM (granite3.3:8b) → JSON Parse → Draft Preview → Type Creation
```

### Current Capabilities

- ✅ Basic field generation (3-7 fields)
- ✅ Simple Mustache templates
- ✅ Basic extraction instructions
- ✅ Tag and descriptor suggestions

### Current Limitations

- ❌ **Field count constraint**: Hard-coded "3-7 fields" in prompt
- ❌ **Token limit**: 700 tokens insufficient for rich outputs
- ❌ **No field metadata**: Just field names, no descriptions/types
- ❌ **Flat templates**: No semantic sections or markdown structure
- ❌ **Generic instructions**: Not role-based or context-aware

### Key Files

- `Services/AnalysisTypeAuthoringService.cs` - Backend generation logic
- `Contracts/AiAuthoringContracts.cs` - Request/response structures
- `wwwroot/js/AICreateTypeModal.js` - Frontend UI
- `Controllers/AnalysisTypesController.cs` - API endpoint

## 3. Gap Analysis

### User Vision (Enterprise Architecture Review Example)

**Instructions** (role-based, contextual):

```
As an Enterprise Architect at Geisinger, you are conducting a thorough
evaluation of a proposed technology or system. Your goal is to assess
alignment with organizational strategies, identify risks, and provide
actionable recommendations.
```

**Template** (structured markdown with sections):

```markdown
# {{title}}

## Review Details

- **Document Title**: {{document_title}}
- **Review Date**: {{review_date}}
- **Reviewed By**: {{reviewer_name}}

## Executive Summary

{{executive_summary}}

## Strategic Alignment

### Business Goals

{{business_goals_alignment}}
[... 8 more sections with 20+ total fields ...]
```

**Field Metadata**:

- Each field has description, type, validation hints
- Examples: `reviewer_name` (text, required), `risk_level` (enum: Low|Medium|High|Critical)

### Current System Output (Basic Type Example)

**Instructions**:

```
Extract meeting information including attendees, agenda, decisions.
```

**Template**:

```
Meeting: {{title}}
Date: {{date}}
Attendees: {{attendees}}
Summary: {{summary}}
```

**Fields**: `["title", "date", "attendees", "summary"]` (no metadata)

## 4. Enhancement Strategy

### Phase 1: Backend Prompt Engineering (Quick Win)

**Goal**: Enhance prompt to request richer outputs within existing architecture.

**Changes to `AnalysisTypeAuthoringService.cs`**:

1. **Remove field count constraint**

   - Change: "CRITICAL: Define 3-7 output field names" → "Define 5-20 output field names based on analysis complexity"

2. **Add structured template guidance**

   ```
   TEMPLATE REQUIREMENTS:
   - Use markdown with semantic sections (##, ###)
   - Include field descriptions as comments
   - Group related fields logically
   - Use tables for structured data
   ```

3. **Add role-based instruction guidance**

   ```
   INSTRUCTION REQUIREMENTS:
   - Begin with role context ("As a [role]...")
   - Explain the analysis purpose and audience
   - Provide specific extraction criteria
   - Include quality expectations
   ```

4. **Increase token limits**

   - MaxTokens: 700 → 2000
   - Consider chunked generation for very complex types

5. **Enhance JSON schema in prompt**
   ```json
   {
     "name": "string",
     "description": "string",
     "tags": ["string"],
     "descriptors": ["string"],
     "instructions": "string (role-based, 200-500 words)",
     "outputFields": [
       {
         "name": "fieldName",
         "description": "what this field represents",
         "type": "string|number|list|enum",
         "required": true|false,
         "enumValues": ["value1", "value2"] // if type=enum
       }
     ],
     "outputTemplate": "string (markdown with sections)",
     "requiredSourceTypes": ["string"]
   }
   ```

**Effort**: 2-4 hours  
**Risk**: Low (prompt changes only)  
**Benefit**: Immediate improvement in output quality

### Phase 2: Contract & Schema Enhancement (Medium Term)

**Goal**: Extend data contracts to support field metadata and structured templates.

**New Contracts** (`Contracts/AiAuthoringContracts.cs`):

```csharp
public record OutputFieldMetadata
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public FieldType Type { get; init; } = FieldType.String;
    public bool Required { get; init; } = true;
    public string[]? EnumValues { get; init; }
    public string? ValidationHint { get; init; }
}

public enum FieldType
{
    String,
    Number,
    List,
    Enum,
    Date,
    Markdown
}

public record TemplateSection
{
    public string Name { get; init; } = string.Empty;
    public int Order { get; init; }
    public string Content { get; init; } = string.Empty;
    public string[]? FieldNames { get; init; }
}

public record AnalysisTypeDraft
{
    // Existing properties...
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Instructions { get; init; } = string.Empty;

    // Enhanced properties
    public OutputFieldMetadata[] OutputFieldsMetadata { get; init; } = [];
    public TemplateSection[] TemplateSections { get; init; } = [];
    public string RoleContext { get; init; } = string.Empty; // "Enterprise Architect"

    // Backward compatibility
    public string[] OutputFields => OutputFieldsMetadata.Select(f => f.Name).ToArray();
    public string OutputTemplate { get; init; } = string.Empty; // Flat fallback
}
```

**Service Changes** (`AnalysisTypeAuthoringService.cs`):

```csharp
private AnalysisTypeDraft ParseDraft(string responseText)
{
    var json = ExtractJson(responseText);
    var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    // Parse field metadata
    var fieldsMetadata = root.GetProperty("outputFields")
        .EnumerateArray()
        .Select(field => new OutputFieldMetadata
        {
            Name = CanonicalizeFieldName(field.GetProperty("name").GetString()!),
            Description = field.GetProperty("description").GetString() ?? "",
            Type = Enum.Parse<FieldType>(field.GetProperty("type").GetString() ?? "String"),
            Required = field.TryGetProperty("required", out var req) && req.GetBoolean(),
            EnumValues = field.TryGetProperty("enumValues", out var ev)
                ? ev.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : null
        })
        .ToArray();

    // Parse template sections (if provided)
    var sections = root.TryGetProperty("templateSections", out var sectionsEl)
        ? sectionsEl.EnumerateArray()
            .Select(s => new TemplateSection
            {
                Name = s.GetProperty("name").GetString()!,
                Order = s.GetProperty("order").GetInt32(),
                Content = s.GetProperty("content").GetString()!
            })
            .ToArray()
        : Array.Empty<TemplateSection>();

    return new AnalysisTypeDraft
    {
        // ... existing parsing ...
        OutputFieldsMetadata = fieldsMetadata,
        TemplateSections = sections,
        RoleContext = ExtractRoleFromInstructions(instructions)
    };
}
```

**Effort**: 1-2 days  
**Risk**: Medium (schema migration needed)  
**Benefit**: Rich metadata enables advanced UI features

### Phase 3: UI Enhancement (User Experience)

**Goal**: Display and edit rich field metadata and structured templates.

**Preview Modal Enhancements** (`AICreateTypeModal.js`):

```javascript
renderFieldsMetadata(fields) {
    const tbody = fields.map(field => `
        <tr>
            <td><code>${field.name}</code></td>
            <td>${field.description}</td>
            <td><span class="badge badge-${field.type.toLowerCase()}">${field.type}</span></td>
            <td>${field.required ? '<span class="badge badge-required">Required</span>' : 'Optional'}</td>
            ${field.enumValues ? `<td>${field.enumValues.join(', ')}</td>` : '<td>-</td>'}
        </tr>
    `).join('');

    return `
        <table class="fields-metadata-table">
            <thead>
                <tr>
                    <th>Field</th>
                    <th>Description</th>
                    <th>Type</th>
                    <th>Requirement</th>
                    <th>Values</th>
                </tr>
            </thead>
            <tbody>${tbody}</tbody>
        </table>
    `;
}

renderTemplateSections(sections) {
    return sections
        .sort((a, b) => a.order - b.order)
        .map(section => `
            <div class="template-section">
                <h4>${section.name}</h4>
                <pre><code class="language-markdown">${escapeHtml(section.content)}</code></pre>
            </div>
        `)
        .join('');
}

showPreviewStep(data) {
    const previewHtml = `
        <div class="preview-container">
            <!-- Role Context -->
            <div class="preview-role">
                <h3>Role Context</h3>
                <p><strong>Persona:</strong> ${data.roleContext || 'Generic Analyst'}</p>
            </div>

            <!-- Instructions -->
            <div class="preview-instructions">
                <h3>Processing Instructions</h3>
                <div class="instructions-content">
                    ${marked.parse(data.instructions)}
                </div>
            </div>

            <!-- Field Metadata Table -->
            <div class="preview-fields">
                <h3>Output Fields (${data.outputFieldsMetadata.length})</h3>
                ${this.renderFieldsMetadata(data.outputFieldsMetadata)}
            </div>

            <!-- Template Sections -->
            <div class="preview-template">
                <h3>Output Template</h3>
                ${data.templateSections.length > 0
                    ? this.renderTemplateSections(data.templateSections)
                    : `<pre><code class="language-markdown">${escapeHtml(data.outputTemplate)}</code></pre>`
                }
            </div>
        </div>
    `;

    document.getElementById('modalContent').innerHTML = previewHtml;
}
```

**CSS Additions** (`wwwroot/css/ai-create-modal.css`):

```css
.fields-metadata-table {
  width: 100%;
  border-collapse: collapse;
  margin: 1rem 0;
  font-size: 0.9rem;
}

.fields-metadata-table th,
.fields-metadata-table td {
  padding: 0.75rem;
  text-align: left;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
}

.fields-metadata-table th {
  font-weight: 600;
  color: var(--color-text-secondary);
  background: rgba(255, 255, 255, 0.03);
}

.badge-string {
  background: var(--color-blue-500);
}
.badge-number {
  background: var(--color-green-500);
}
.badge-list {
  background: var(--color-purple-500);
}
.badge-enum {
  background: var(--color-orange-500);
}
.badge-required {
  background: var(--color-red-500);
}

.template-section {
  margin-bottom: 1.5rem;
  padding: 1rem;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 8px;
}

.template-section h4 {
  margin: 0 0 0.75rem 0;
  color: var(--color-accent-primary);
}

.instructions-content {
  line-height: 1.6;
  color: var(--color-text-primary);
}
```

**Effort**: 2-3 days  
**Risk**: Low (UI enhancements)  
**Benefit**: Users see and understand rich metadata immediately

### Phase 4: Advanced Features (Future)

**Field Metadata Editor**:

- Allow users to refine AI-generated field descriptions
- Add validation rules
- Reorder fields and sections

**Template Section Editor**:

- Visual markdown editor with preview
- Drag-and-drop section reordering
- Field insertion helpers

**Role Library**:

- Pre-defined role contexts (Enterprise Architect, Compliance Officer, etc.)
- Custom role creation

**Example Gallery**:

- Show example rich types in UI
- "Use as template" feature

## 5. Implementation Roadmap

### Sprint 1 (1-2 days)

- [ ] Backend: Update prompt in `AnalysisTypeAuthoringService.BuildPrompt()`
- [ ] Backend: Increase MaxTokens to 2000
- [ ] Backend: Remove 3-7 field constraint
- [ ] Backend: Add structured template guidance
- [ ] Test: Generate Enterprise Architecture Review example
- [ ] Test: Validate with 3-4 diverse analysis types

### Sprint 2 (2-3 days)

- [ ] Contracts: Add `OutputFieldMetadata` class
- [ ] Contracts: Add `TemplateSection` class
- [ ] Contracts: Extend `AnalysisTypeDraft`
- [ ] Service: Implement enhanced JSON parsing
- [ ] Service: Implement role extraction
- [ ] Migration: Ensure backward compatibility
- [ ] Test: Integration tests with new schema

### Sprint 3 (2-3 days)

- [ ] UI: Create field metadata table component
- [ ] UI: Create template sections renderer
- [ ] UI: Update preview step with new sections
- [ ] CSS: Add styling for metadata table and sections
- [ ] Test: End-to-end user flow
- [ ] Docs: Update user guide with examples

### Sprint 4 (Future - Optional)

- [ ] UI: Field metadata editor
- [ ] UI: Template section editor
- [ ] Backend: Role library
- [ ] UI: Example gallery

## 6. Success Metrics

### Quantitative

- ✅ Generate types with 15-20 fields (vs. current 3-7)
- ✅ Instructions 200-500 words (vs. current 50-100)
- ✅ Template with 5+ semantic sections
- ✅ 90%+ user satisfaction with generated types

### Qualitative

- ✅ Generated types match Enterprise Architecture Review quality
- ✅ Role-based instructions provide clear context
- ✅ Field descriptions help users understand data structure
- ✅ Templates use markdown effectively for readability

## 7. Risk Mitigation

| Risk                                | Impact | Likelihood | Mitigation                                                      |
| ----------------------------------- | ------ | ---------- | --------------------------------------------------------------- |
| LLM output quality varies           | High   | Medium     | Add output validation, allow regeneration, provide editing UI   |
| Token limits hit with complex types | Medium | Medium     | Implement chunked generation, increase model context window     |
| Backward compatibility breaks       | High   | Low        | Maintain dual schema support, migration path for existing types |
| UI complexity overwhelms users      | Medium | Low        | Progressive disclosure, default to simple view, advanced toggle |

## 8. Testing Strategy

### Unit Tests

```csharp
[Fact]
public void ParseDraft_WithFieldMetadata_ParsesCorrectly()
{
    var json = """
    {
        "name": "Test Type",
        "outputFields": [
            {
                "name": "reviewerName",
                "description": "Name of the reviewer",
                "type": "string",
                "required": true
            }
        ]
    }
    """;

    var draft = service.ParseDraft(json);

    Assert.Single(draft.OutputFieldsMetadata);
    Assert.Equal("reviewer_name", draft.OutputFieldsMetadata[0].Name);
    Assert.Equal("Name of the reviewer", draft.OutputFieldsMetadata[0].Description);
}
```

### Integration Tests

- Generate Enterprise Architecture Review type
- Generate Security Assessment type
- Generate Compliance Audit type
- Verify all have rich templates and instructions

### User Acceptance Testing

- Provide test users with enhancement
- Ask to create 5 analysis types from scratch
- Measure time to create, quality of output, satisfaction

## 9. Documentation Requirements

### User Documentation

- **Guide**: "Creating Rich Analysis Types with AI"
- **Reference**: Updated API documentation for new schemas
- **Examples**: Gallery of rich analysis types

### Technical Documentation

- **ADR**: Document decision to extend AI authoring capabilities
- **API Reference**: Document new contract schemas
- **Migration Guide**: For existing types (if needed)

### Code Documentation

- Inline comments explaining prompt structure
- JSDoc for new UI rendering methods
- XML comments for new C# contracts

## 10. Conclusion

This enhancement transforms Meridian's AI authoring from a **basic type generator** into a **professional analysis type design assistant**. By extending prompts, enriching schemas, and enhancing the UI, users will create production-quality analysis types that match the Enterprise Architecture Review benchmark.

**Recommendation**: Proceed with **Phase 1 (Backend Prompt Engineering)** immediately as a quick win, then assess user feedback before committing to Phases 2-3.

---

**Next Steps**:

1. Review this plan with stakeholders
2. Approve Phase 1 implementation
3. Create Sprint 1 tasks
4. Begin prompt engineering enhancement
