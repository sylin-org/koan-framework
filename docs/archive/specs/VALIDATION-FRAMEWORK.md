# Documentation Validation Framework

## Purpose

Every document in `/documentation/` (except ADRs) MUST be validated for technical correctness against the current framework version. This ensures documentation serves as reliable guidance rather than outdated reference material.

## Validation Requirements

### 1. Content Accuracy Validation

#### Code Examples
- **Syntax Check**: All code examples must compile without errors
- **Framework Compatibility**: Code must work with specified framework version
- **Import Statements**: Include all necessary using statements and dependencies
- **Convention Compliance**: Follow established Koan Framework patterns

#### API References
- **Method Signatures**: Verify method names, parameters, and return types
- **Class Names**: Ensure class and interface names match current framework
- **Namespace Accuracy**: Verify correct namespace references
- **Property Names**: Check property and field names for accuracy

#### Configuration Examples
- **JSON Validity**: All JSON configuration examples must be valid
- **Schema Compliance**: Configuration must match framework's expected structure
- **Environment Variables**: Verify environment variable names and formats
- **Default Values**: Ensure default values match framework defaults

### 2. Framework Version Compatibility

#### Version Tracking
- Document frontmatter must specify compatible framework versions
- Breaking changes between versions must be documented
- Deprecated features must be clearly marked
- Migration guidance for version upgrades

#### Feature Availability
- Verify features exist in specified framework version
- Check if features are experimental, stable, or deprecated
- Note any preview/beta functionality clearly
- Document any package dependencies

### 3. Link Integrity

#### Internal Links
- All internal documentation links must resolve correctly
- Cross-references between documents must be bidirectional where appropriate
- Archive links must point to correct historical versions
- Navigation paths must be logical and complete

#### External Links
- Framework repository links must be current
- NuGet package links must point to correct versions
- Sample repository links must be functional
- Third-party service documentation links must be current

### 4. Example Testing

#### Runnable Examples
- Code examples should be testable in isolation
- Configuration examples should work in clean environment
- Multi-step procedures must be validated end-to-end
- Dependencies and prerequisites must be clearly stated

## Validation Process

### Phase 1: Automated Validation

```bash
# Syntax validation for code blocks
./scripts/validate-code-examples.sh

# Link integrity checking
./scripts/validate-links.sh

# JSON configuration validation
./scripts/validate-configs.sh

# Framework version compatibility check
./scripts/check-framework-compatibility.sh
```

### Phase 2: Manual Review

#### Content Review Checklist
- [ ] Code examples compile and execute correctly
- [ ] Configuration examples work in test environment
- [ ] API references match current framework documentation
- [ ] Conceptual explanations are accurate and complete
- [ ] Prerequisites and dependencies are correctly stated
- [ ] Troubleshooting sections address real issues

#### Technical Accuracy Review
- [ ] Framework patterns are correctly demonstrated
- [ ] Security considerations are properly addressed
- [ ] Performance implications are accurately described
- [ ] Error handling examples are appropriate
- [ ] Best practices align with current framework guidance

### Phase 3: Environment Testing

#### Clean Environment Validation
- Test all examples in fresh framework installation
- Verify examples work without additional setup
- Confirm configuration examples produce expected results
- Validate that prerequisites are sufficient

## Validation Tracking

### Document Frontmatter
```yaml
validation:
  last_tested: "2025-01-17"
  framework_version: "v0.2.18"
  validator: "username"
  test_environment: "clean-install"
  known_issues: []
```

### Validation Log
```yaml
# .validation-log.yml
documents:
  - path: "reference/data/entity-patterns.md"
    last_validated: "2025-01-17"
    framework_version: "v0.2.18"
    validator: "technical-reviewer"
    status: "passed"
    issues: []

  - path: "guides/authentication-setup.md"
    last_validated: "2025-01-17"
    framework_version: "v0.2.18"
    validator: "technical-reviewer"
    status: "passed-with-notes"
    issues:
      - "Example 3 requires additional package reference"
      - "Configuration section needs update for new auth flow"
```

## Validation Automation

### CI/CD Integration
```yaml
# .github/workflows/docs-validation.yml
name: Documentation Validation

on:
  pull_request:
    paths: ['documentation/**']

jobs:
  validate-docs:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'

      - name: Validate Code Examples
        run: ./scripts/validate-code-examples.sh

      - name: Check Links
        run: ./scripts/validate-links.sh

      - name: Test Configurations
        run: ./scripts/validate-configs.sh

      - name: Framework Compatibility
        run: ./scripts/check-framework-compatibility.sh
```

### Validation Tools

#### Code Example Validator
```bash
#!/bin/bash
# scripts/validate-code-examples.sh

# Extract C# code blocks from markdown files
# Create temporary .cs files
# Compile against current framework version
# Report compilation errors

for file in documentation/**/*.md; do
    echo "Validating code examples in $file"
    # Extract code blocks and validate
done
```

#### Configuration Validator
```bash
#!/bin/bash
# scripts/validate-configs.sh

# Extract JSON configuration blocks
# Validate JSON syntax
# Test against framework configuration schema
# Report validation errors

for file in documentation/**/*.md; do
    echo "Validating configurations in $file"
    # Extract and validate config blocks
done
```

## Quality Gates

### Pre-Merge Requirements
- All code examples must compile successfully
- All configuration examples must be valid JSON
- All internal links must resolve correctly
- Framework version compatibility must be verified
- Manual review must be completed by technical reviewer

### Periodic Re-validation
- **Monthly**: Re-validate all documents for link integrity
- **Per Framework Release**: Full validation of all technical content
- **Quarterly**: Manual review of high-traffic documentation

### Validation Failure Response
1. **Immediate**: Mark document as requiring validation in frontmatter
2. **Document**: Record specific issues in validation log
3. **Prioritize**: Create remediation tasks based on document importance
4. **Communicate**: Notify document maintainers of validation failures

## Exception Handling

### ADR Exception
Architecture Decision Records (ADRs) are exempt from correctness validation as they:
- Document historical decisions and context
- May reference deprecated or changed implementations
- Serve as historical record rather than current guidance
- Are marked with decision status (Proposed/Accepted/Superseded)

### Archive Content Exception
Documents in `/archive/` are exempt from current correctness validation but must:
- Be clearly marked as historical content
- Include archival date and reason
- Reference current documentation for up-to-date guidance
- Maintain link integrity where possible

## Validation Metrics

### Success Metrics
- **Coverage**: % of documents with current validation status
- **Accuracy**: % of documents passing validation on first attempt
- **Freshness**: Average time since last validation
- **Completeness**: % of documents with all required validation elements

### Quality Indicators
- Compilation success rate for code examples
- Configuration validation success rate
- Link integrity percentage
- User feedback on documentation accuracy

This validation framework ensures documentation serves as a reliable, accurate resource for framework users while maintaining quality standards as the framework evolves.