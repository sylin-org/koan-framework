using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;

namespace Koan.Samples.Meridian.SeedData;

/// <summary>
/// Seed data for default AnalysisType templates.
/// These define standard analysis methodologies with prompts and deliverable formats.
/// </summary>
public static class AnalysisTypeSeedData
{
    public static AnalysisType[] GetAnalysisTypes() => new[]
    {
        new AnalysisType
        {
            Id = "EnterpriseArchitectureReview",
            Name = "Enterprise Architecture Review",
            Code = "EAR",
            Description = "A review to assess proposed solutions or investments from an enterprise architecture perspective, ensuring alignment with existing systems, policies, and strategic objectives.",
            Version = 1,
            Tags = new List<string>
            {
                "enterprise-architecture",
                "architecture-review",
                "solution-assessment",
                "compliance",
                "infrastructure",
                "security-architecture",
                "strategic-alignment",
                "governance"
            },
            Descriptors = new List<string>
            {
                "Architecture assessment",
                "Solution validation",
                "Strategic alignment review"
            },
            Instructions = @"As an Enterprise Architect, provide an initial analysis and recommendation based on the provided information.

Analyze the provided documents, which detail an Enterprise Architecture Review. Populate the template fields by extracting information related to:


The associated documents are meant to provide you with contextual information to drive your assessment.

If any of the expected data elements of the template can't be inferred from the text, clearly identify it as missing.

Provide insights on strategic architecture opportunities, if any.",
            OutputTemplate = @"# Enterprise Architecture Review


## Review Details
**ServiceNow ID:** {{SERVICENOW_ID}}
**Architect:** {{ARCHITECT}}
**Review Date:** {{REVIEW_DATE}}
**Stakeholders:** {{STAKEHOLDERS}}
**Contributors:** {{CONTRIBUTORS}}
**Recommendation Status:** {{RECOMMENDATION_STATUS}}


## Review Questions
**Business/Solution Requirements Validated:** {{REQUIREMENTS_VALIDATED}}
**Existing Standard IT Service Addresses Need:** {{EXISTING_IT_SERVICE}}
**Existing System:** {{EXISTING_SYSTEM}}
**Can Existing Solution Be Changed/Expanded:** {{CHANGE_EXISTING_SOLUTION}}
**New Solution Needs Designed:** {{NEW_SOLUTION_NEEDED}}


## Request Overview
{{REQUEST_OVERVIEW}}


## Application Architecture
{{APPLICATION_ARCHITECTURE}}


## Infrastructure Architecture
{{INFRASTRUCTURE_ARCHITECTURE}}


## Security Architecture
{{SECURITY_ARCHITECTURE}}


## Support
{{SUPPORT}}


## Recommendation
**Overall Recommendation:** {{OVERALL_RECOMMENDATION}}
**Reasoning:** {{RECOMMENDATION_REASONING}}


## Future Consideration
{{FUTURE_CONSIDERATION}}


## Reference Documentation
{{REFERENCE_DOCUMENTATION}}",
            JsonSchema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""SERVICENOW_ID"": { ""type"": ""string"" },
    ""ARCHITECT"": { ""type"": ""string"" },
    ""REVIEW_DATE"": { ""type"": ""string"", ""format"": ""date"" },
    ""STAKEHOLDERS"": { ""type"": ""string"" },
    ""CONTRIBUTORS"": { ""type"": ""string"" },
    ""RECOMMENDATION_STATUS"": { ""type"": ""string"", ""enum"": [""Approved"", ""Conditional"", ""Not Approved"", ""Needs More Information""] },
    ""REQUIREMENTS_VALIDATED"": { ""type"": ""string"" },
    ""EXISTING_IT_SERVICE"": { ""type"": ""string"" },
    ""EXISTING_SYSTEM"": { ""type"": ""string"" },
    ""CHANGE_EXISTING_SOLUTION"": { ""type"": ""string"" },
    ""NEW_SOLUTION_NEEDED"": { ""type"": ""string"" },
    ""REQUEST_OVERVIEW"": { ""type"": ""string"" },
    ""APPLICATION_ARCHITECTURE"": { ""type"": ""string"" },
    ""INFRASTRUCTURE_ARCHITECTURE"": { ""type"": ""string"" },
    ""SECURITY_ARCHITECTURE"": { ""type"": ""string"" },
    ""SUPPORT"": { ""type"": ""string"" },
    ""OVERALL_RECOMMENDATION"": { ""type"": ""string"" },
    ""RECOMMENDATION_REASONING"": { ""type"": ""string"" },
    ""FUTURE_CONSIDERATION"": { ""type"": ""string"" },
    ""REFERENCE_DOCUMENTATION"": { ""type"": ""string"" }
  },
  ""required"": [""SERVICENOW_ID"", ""ARCHITECT"", ""REVIEW_DATE"", ""RECOMMENDATION_STATUS"", ""OVERALL_RECOMMENDATION""]
}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        },

        new AnalysisType
        {
            Id = "VendorDueDiligence",
            Name = "Vendor Due Diligence",
            Code = "VDD",
            Description = "A comprehensive assessment of potential vendors to evaluate their capabilities, financial stability, compliance posture, and operational readiness before entering into a business relationship.",
            Version = 1,
            Tags = new List<string>
            {
                "vendor-assessment",
                "due-diligence",
                "risk-management",
                "compliance",
                "procurement",
                "vendor-selection",
                "third-party-risk"
            },
            Descriptors = new List<string>
            {
                "Vendor evaluation",
                "Third-party assessment",
                "Supplier qualification"
            },
            Instructions = @"As a Vendor Risk Analyst, conduct a thorough due diligence assessment of the vendor based on the provided documentation.

Extract and analyze information across the following dimensions:

- Vendor identification and corporate structure
- Financial health and stability indicators
- Compliance certifications and regulatory adherence
- Security posture and data protection capabilities
- Operational capacity and service delivery track record
- References and customer testimonials
- Risk factors and mitigation strategies

Provide a clear recommendation on vendor suitability for the intended engagement.

Flag any missing critical information that should be obtained before proceeding.",
            OutputTemplate = @"# Vendor Due Diligence Report

---

## Vendor Identification
**Vendor Name:** {{VENDOR_NAME}}
**Legal Entity:** {{LEGAL_ENTITY}}
**Primary Contact:** {{PRIMARY_CONTACT}}
**Website:** {{WEBSITE}}
**Years in Business:** {{YEARS_IN_BUSINESS}}

---

## Financial Assessment
**Annual Revenue:** {{ANNUAL_REVENUE}}
**Employee Count:** {{EMPLOYEE_COUNT}}
**Financial Stability Rating:** {{FINANCIAL_STABILITY}}
**Key Financial Observations:** {{FINANCIAL_OBSERVATIONS}}

---

## Compliance & Certifications
**ISO Certifications:** {{ISO_CERTIFICATIONS}}
**SOC Reports:** {{SOC_REPORTS}}
**Regulatory Compliance:** {{REGULATORY_COMPLIANCE}}
**Industry-Specific Certifications:** {{INDUSTRY_CERTIFICATIONS}}

---

## Security Posture
**Data Protection Measures:** {{DATA_PROTECTION}}
**Incident Response Capability:** {{INCIDENT_RESPONSE}}
**Security Training Programs:** {{SECURITY_TRAINING}}
**Penetration Testing:** {{PENETRATION_TESTING}}

---

## Operational Capacity
**Service Delivery Model:** {{SERVICE_DELIVERY_MODEL}}
**Geographic Coverage:** {{GEOGRAPHIC_COVERAGE}}
**Capacity to Scale:** {{CAPACITY_TO_SCALE}}
**Support Availability:** {{SUPPORT_AVAILABILITY}}

---

## References & Track Record
**Client References:** {{CLIENT_REFERENCES}}
**Success Stories:** {{SUCCESS_STORIES}}
**Industry Reputation:** {{INDUSTRY_REPUTATION}}

---

## Risk Assessment
**Identified Risks:** {{IDENTIFIED_RISKS}}
**Risk Level:** {{RISK_LEVEL}}
**Mitigation Recommendations:** {{MITIGATION_RECOMMENDATIONS}}

---

## Overall Recommendation
**Recommendation:** {{OVERALL_RECOMMENDATION}}
**Reasoning:** {{RECOMMENDATION_REASONING}}
**Conditions (if any):** {{CONDITIONS}}",
            JsonSchema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""VENDOR_NAME"": { ""type"": ""string"" },
    ""LEGAL_ENTITY"": { ""type"": ""string"" },
    ""PRIMARY_CONTACT"": { ""type"": ""string"" },
    ""WEBSITE"": { ""type"": ""string"", ""format"": ""uri"" },
    ""YEARS_IN_BUSINESS"": { ""type"": ""number"" },
    ""ANNUAL_REVENUE"": { ""type"": ""string"" },
    ""EMPLOYEE_COUNT"": { ""type"": ""string"" },
    ""FINANCIAL_STABILITY"": { ""type"": ""string"", ""enum"": [""Excellent"", ""Good"", ""Fair"", ""Poor"", ""Unknown""] },
    ""FINANCIAL_OBSERVATIONS"": { ""type"": ""string"" },
    ""ISO_CERTIFICATIONS"": { ""type"": ""string"" },
    ""SOC_REPORTS"": { ""type"": ""string"" },
    ""REGULATORY_COMPLIANCE"": { ""type"": ""string"" },
    ""INDUSTRY_CERTIFICATIONS"": { ""type"": ""string"" },
    ""DATA_PROTECTION"": { ""type"": ""string"" },
    ""INCIDENT_RESPONSE"": { ""type"": ""string"" },
    ""SECURITY_TRAINING"": { ""type"": ""string"" },
    ""PENETRATION_TESTING"": { ""type"": ""string"" },
    ""SERVICE_DELIVERY_MODEL"": { ""type"": ""string"" },
    ""GEOGRAPHIC_COVERAGE"": { ""type"": ""string"" },
    ""CAPACITY_TO_SCALE"": { ""type"": ""string"" },
    ""SUPPORT_AVAILABILITY"": { ""type"": ""string"" },
    ""CLIENT_REFERENCES"": { ""type"": ""string"" },
    ""SUCCESS_STORIES"": { ""type"": ""string"" },
    ""INDUSTRY_REPUTATION"": { ""type"": ""string"" },
    ""IDENTIFIED_RISKS"": { ""type"": ""string"" },
    ""RISK_LEVEL"": { ""type"": ""string"", ""enum"": [""Low"", ""Medium"", ""High"", ""Critical""] },
    ""MITIGATION_RECOMMENDATIONS"": { ""type"": ""string"" },
    ""OVERALL_RECOMMENDATION"": { ""type"": ""string"", ""enum"": [""Highly Recommended"", ""Recommended"", ""Conditional"", ""Not Recommended""] },
    ""RECOMMENDATION_REASONING"": { ""type"": ""string"" },
    ""CONDITIONS"": { ""type"": ""string"" }
  },
  ""required"": [""VENDOR_NAME"", ""RISK_LEVEL"", ""OVERALL_RECOMMENDATION""]
}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        },

        new AnalysisType
        {
            Id = "SecurityAssessment",
            Name = "Security Assessment",
            Code = "SEC",
            Description = "A thorough evaluation of security controls, vulnerabilities, and risk posture to ensure systems and processes meet security standards and protect against threats.",
            Version = 1,
            Tags = new List<string>
            {
                "security",
                "vulnerability-assessment",
                "risk-analysis",
                "penetration-testing",
                "security-controls",
                "threat-modeling",
                "compliance"
            },
            Descriptors = new List<string>
            {
                "Security evaluation",
                "Risk assessment",
                "Vulnerability analysis"
            },
            Instructions = @"As a Security Analyst, evaluate the security posture based on the provided documentation.

Analyze and extract information covering:

- System architecture and security boundaries
- Authentication and authorization mechanisms
- Data protection and encryption practices
- Network security controls
- Vulnerability findings and severity ratings
- Compliance with security frameworks (NIST, CIS, ISO 27001)
- Incident response and recovery capabilities
- Recommended remediation actions prioritized by risk

Provide an overall security rating and actionable recommendations.",
            OutputTemplate = @"# Security Assessment Report

---

## Assessment Overview
**System/Application:** {{SYSTEM_NAME}}
**Assessment Date:** {{ASSESSMENT_DATE}}
**Lead Assessor:** {{LEAD_ASSESSOR}}
**Assessment Type:** {{ASSESSMENT_TYPE}}

---

## Scope & Methodology
**Scope:** {{SCOPE}}
**Methodology:** {{METHODOLOGY}}
**Tools Used:** {{TOOLS_USED}}

---

## Architecture Review
**System Architecture:** {{SYSTEM_ARCHITECTURE}}
**Security Boundaries:** {{SECURITY_BOUNDARIES}}
**Trust Zones:** {{TRUST_ZONES}}

---

## Authentication & Authorization
**Authentication Mechanisms:** {{AUTHENTICATION}}
**Authorization Model:** {{AUTHORIZATION}}
**Multi-Factor Authentication:** {{MFA_STATUS}}
**Session Management:** {{SESSION_MANAGEMENT}}

---

## Data Protection
**Encryption at Rest:** {{ENCRYPTION_AT_REST}}
**Encryption in Transit:** {{ENCRYPTION_IN_TRANSIT}}
**Data Classification:** {{DATA_CLASSIFICATION}}
**Sensitive Data Handling:** {{SENSITIVE_DATA_HANDLING}}

---

## Network Security
**Firewall Configuration:** {{FIREWALL_CONFIG}}
**Network Segmentation:** {{NETWORK_SEGMENTATION}}
**Intrusion Detection:** {{INTRUSION_DETECTION}}
**DMZ Configuration:** {{DMZ_CONFIG}}

---

## Vulnerability Findings
**Critical Vulnerabilities:** {{CRITICAL_VULNERABILITIES}}
**High Vulnerabilities:** {{HIGH_VULNERABILITIES}}
**Medium Vulnerabilities:** {{MEDIUM_VULNERABILITIES}}
**Low Vulnerabilities:** {{LOW_VULNERABILITIES}}

---

## Compliance Mapping
**NIST Compliance:** {{NIST_COMPLIANCE}}
**CIS Controls:** {{CIS_CONTROLS}}
**ISO 27001 Alignment:** {{ISO_27001}}
**Regulatory Requirements:** {{REGULATORY_REQUIREMENTS}}

---

## Incident Response
**IR Plan Status:** {{IR_PLAN_STATUS}}
**Backup & Recovery:** {{BACKUP_RECOVERY}}
**Business Continuity:** {{BUSINESS_CONTINUITY}}

---

## Overall Security Rating
**Rating:** {{SECURITY_RATING}}
**Risk Level:** {{RISK_LEVEL}}

---

## Recommendations
**Immediate Actions:** {{IMMEDIATE_ACTIONS}}
**Short-term Improvements:** {{SHORT_TERM_IMPROVEMENTS}}
**Long-term Strategy:** {{LONG_TERM_STRATEGY}}",
            JsonSchema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""SYSTEM_NAME"": { ""type"": ""string"" },
    ""ASSESSMENT_DATE"": { ""type"": ""string"", ""format"": ""date"" },
    ""LEAD_ASSESSOR"": { ""type"": ""string"" },
    ""ASSESSMENT_TYPE"": { ""type"": ""string"", ""enum"": [""Penetration Test"", ""Vulnerability Scan"", ""Security Audit"", ""Risk Assessment""] },
    ""SCOPE"": { ""type"": ""string"" },
    ""METHODOLOGY"": { ""type"": ""string"" },
    ""TOOLS_USED"": { ""type"": ""string"" },
    ""SYSTEM_ARCHITECTURE"": { ""type"": ""string"" },
    ""SECURITY_BOUNDARIES"": { ""type"": ""string"" },
    ""TRUST_ZONES"": { ""type"": ""string"" },
    ""AUTHENTICATION"": { ""type"": ""string"" },
    ""AUTHORIZATION"": { ""type"": ""string"" },
    ""MFA_STATUS"": { ""type"": ""string"", ""enum"": [""Enabled"", ""Partial"", ""Not Enabled""] },
    ""SESSION_MANAGEMENT"": { ""type"": ""string"" },
    ""ENCRYPTION_AT_REST"": { ""type"": ""string"" },
    ""ENCRYPTION_IN_TRANSIT"": { ""type"": ""string"" },
    ""DATA_CLASSIFICATION"": { ""type"": ""string"" },
    ""SENSITIVE_DATA_HANDLING"": { ""type"": ""string"" },
    ""FIREWALL_CONFIG"": { ""type"": ""string"" },
    ""NETWORK_SEGMENTATION"": { ""type"": ""string"" },
    ""INTRUSION_DETECTION"": { ""type"": ""string"" },
    ""DMZ_CONFIG"": { ""type"": ""string"" },
    ""CRITICAL_VULNERABILITIES"": { ""type"": ""number"" },
    ""HIGH_VULNERABILITIES"": { ""type"": ""number"" },
    ""MEDIUM_VULNERABILITIES"": { ""type"": ""number"" },
    ""LOW_VULNERABILITIES"": { ""type"": ""number"" },
    ""NIST_COMPLIANCE"": { ""type"": ""string"" },
    ""CIS_CONTROLS"": { ""type"": ""string"" },
    ""ISO_27001"": { ""type"": ""string"" },
    ""REGULATORY_REQUIREMENTS"": { ""type"": ""string"" },
    ""IR_PLAN_STATUS"": { ""type"": ""string"" },
    ""BACKUP_RECOVERY"": { ""type"": ""string"" },
    ""BUSINESS_CONTINUITY"": { ""type"": ""string"" },
    ""SECURITY_RATING"": { ""type"": ""string"", ""enum"": [""Excellent"", ""Good"", ""Fair"", ""Poor"", ""Critical""] },
    ""RISK_LEVEL"": { ""type"": ""string"", ""enum"": [""Low"", ""Medium"", ""High"", ""Critical""] },
    ""IMMEDIATE_ACTIONS"": { ""type"": ""string"" },
    ""SHORT_TERM_IMPROVEMENTS"": { ""type"": ""string"" },
    ""LONG_TERM_STRATEGY"": { ""type"": ""string"" }
  },
  ""required"": [""SYSTEM_NAME"", ""ASSESSMENT_DATE"", ""SECURITY_RATING"", ""RISK_LEVEL""]
}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        },

        new AnalysisType
        {
            Id = "FinancialAnalysis",
            Name = "Financial Analysis",
            Code = "FIN",
            Description = "A detailed examination of financial statements, ratios, and performance metrics to assess the financial health, profitability, and sustainability of an organization or investment opportunity.",
            Version = 1,
            Tags = new List<string>
            {
                "financial-analysis",
                "financial-statements",
                "profitability",
                "cash-flow",
                "financial-ratios",
                "investment-analysis",
                "performance-metrics"
            },
            Descriptors = new List<string>
            {
                "Financial review",
                "Financial health assessment",
                "Performance analysis"
            },
            Instructions = @"As a Financial Analyst, evaluate the financial position and performance based on the provided financial documents.

Extract and analyze:

- Revenue and expense trends
- Profitability metrics (gross margin, operating margin, net margin)
- Cash flow analysis
- Balance sheet strength (assets, liabilities, equity)
- Financial ratios (liquidity, leverage, efficiency, profitability)
- Year-over-year comparisons
- Industry benchmarks where applicable
- Financial risks and strengths

Provide insights on financial sustainability, growth potential, and any red flags.",
            OutputTemplate = @"# Financial Analysis Report

---

## Company Overview
**Company Name:** {{COMPANY_NAME}}
**Fiscal Year:** {{FISCAL_YEAR}}
**Analysis Period:** {{ANALYSIS_PERIOD}}
**Industry:** {{INDUSTRY}}

---

## Revenue Analysis
**Total Revenue:** {{TOTAL_REVENUE}}
**Revenue Growth (YoY):** {{REVENUE_GROWTH_YOY}}
**Revenue by Segment:** {{REVENUE_BY_SEGMENT}}
**Geographic Revenue:** {{GEOGRAPHIC_REVENUE}}

---

## Profitability Metrics
**Gross Profit:** {{GROSS_PROFIT}}
**Gross Margin:** {{GROSS_MARGIN}}
**Operating Income:** {{OPERATING_INCOME}}
**Operating Margin:** {{OPERATING_MARGIN}}
**Net Income:** {{NET_INCOME}}
**Net Profit Margin:** {{NET_PROFIT_MARGIN}}

---

## Cash Flow Analysis
**Operating Cash Flow:** {{OPERATING_CASH_FLOW}}
**Investing Cash Flow:** {{INVESTING_CASH_FLOW}}
**Financing Cash Flow:** {{FINANCING_CASH_FLOW}}
**Free Cash Flow:** {{FREE_CASH_FLOW}}

---

## Balance Sheet Summary
**Total Assets:** {{TOTAL_ASSETS}}
**Total Liabilities:** {{TOTAL_LIABILITIES}}
**Shareholders' Equity:** {{SHAREHOLDERS_EQUITY}}
**Current Ratio:** {{CURRENT_RATIO}}
**Quick Ratio:** {{QUICK_RATIO}}

---

## Financial Ratios
**Debt-to-Equity:** {{DEBT_TO_EQUITY}}
**Return on Assets (ROA):** {{ROA}}
**Return on Equity (ROE):** {{ROE}}
**Asset Turnover:** {{ASSET_TURNOVER}}
**Inventory Turnover:** {{INVENTORY_TURNOVER}}

---

## Trends & Observations
**Key Trends:** {{KEY_TRENDS}}
**Strengths:** {{STRENGTHS}}
**Weaknesses:** {{WEAKNESSES}}
**Industry Comparison:** {{INDUSTRY_COMPARISON}}

---

## Risk Assessment
**Financial Risks:** {{FINANCIAL_RISKS}}
**Liquidity Concerns:** {{LIQUIDITY_CONCERNS}}
**Leverage Issues:** {{LEVERAGE_ISSUES}}

---

## Overall Assessment
**Financial Health Rating:** {{FINANCIAL_HEALTH_RATING}}
**Investment Recommendation:** {{INVESTMENT_RECOMMENDATION}}
**Summary:** {{SUMMARY}}",
            JsonSchema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""COMPANY_NAME"": { ""type"": ""string"" },
    ""FISCAL_YEAR"": { ""type"": ""string"" },
    ""ANALYSIS_PERIOD"": { ""type"": ""string"" },
    ""INDUSTRY"": { ""type"": ""string"" },
    ""TOTAL_REVENUE"": { ""type"": ""string"" },
    ""REVENUE_GROWTH_YOY"": { ""type"": ""string"" },
    ""REVENUE_BY_SEGMENT"": { ""type"": ""string"" },
    ""GEOGRAPHIC_REVENUE"": { ""type"": ""string"" },
    ""GROSS_PROFIT"": { ""type"": ""string"" },
    ""GROSS_MARGIN"": { ""type"": ""string"" },
    ""OPERATING_INCOME"": { ""type"": ""string"" },
    ""OPERATING_MARGIN"": { ""type"": ""string"" },
    ""NET_INCOME"": { ""type"": ""string"" },
    ""NET_PROFIT_MARGIN"": { ""type"": ""string"" },
    ""OPERATING_CASH_FLOW"": { ""type"": ""string"" },
    ""INVESTING_CASH_FLOW"": { ""type"": ""string"" },
    ""FINANCING_CASH_FLOW"": { ""type"": ""string"" },
    ""FREE_CASH_FLOW"": { ""type"": ""string"" },
    ""TOTAL_ASSETS"": { ""type"": ""string"" },
    ""TOTAL_LIABILITIES"": { ""type"": ""string"" },
    ""SHAREHOLDERS_EQUITY"": { ""type"": ""string"" },
    ""CURRENT_RATIO"": { ""type"": ""string"" },
    ""QUICK_RATIO"": { ""type"": ""string"" },
    ""DEBT_TO_EQUITY"": { ""type"": ""string"" },
    ""ROA"": { ""type"": ""string"" },
    ""ROE"": { ""type"": ""string"" },
    ""ASSET_TURNOVER"": { ""type"": ""string"" },
    ""INVENTORY_TURNOVER"": { ""type"": ""string"" },
    ""KEY_TRENDS"": { ""type"": ""string"" },
    ""STRENGTHS"": { ""type"": ""string"" },
    ""WEAKNESSES"": { ""type"": ""string"" },
    ""INDUSTRY_COMPARISON"": { ""type"": ""string"" },
    ""FINANCIAL_RISKS"": { ""type"": ""string"" },
    ""LIQUIDITY_CONCERNS"": { ""type"": ""string"" },
    ""LEVERAGE_ISSUES"": { ""type"": ""string"" },
    ""FINANCIAL_HEALTH_RATING"": { ""type"": ""string"", ""enum"": [""Excellent"", ""Good"", ""Fair"", ""Poor"", ""Critical""] },
    ""INVESTMENT_RECOMMENDATION"": { ""type"": ""string"", ""enum"": [""Strong Buy"", ""Buy"", ""Hold"", ""Sell"", ""Strong Sell""] },
    ""SUMMARY"": { ""type"": ""string"" }
  },
  ""required"": [""COMPANY_NAME"", ""FISCAL_YEAR"", ""FINANCIAL_HEALTH_RATING""]
}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        },

        new AnalysisType
        {
            Id = "ComplianceReview",
            Name = "Compliance Review",
            Code = "COM",
            Description = "A systematic evaluation of organizational practices, policies, and procedures to ensure adherence to regulatory requirements, industry standards, and internal policies.",
            Version = 1,
            Tags = new List<string>
            {
                "compliance",
                "regulatory-compliance",
                "audit",
                "policy-adherence",
                "risk-management",
                "governance",
                "legal-compliance"
            },
            Descriptors = new List<string>
            {
                "Regulatory compliance assessment",
                "Policy adherence review",
                "Compliance audit"
            },
            Instructions = @"As a Compliance Officer, evaluate the organization's compliance posture based on the provided documentation.

Assess and extract information on:

- Applicable regulatory frameworks and standards
- Current compliance status against requirements
- Policy and procedure documentation
- Control implementation and effectiveness
- Compliance gaps and deficiencies
- Previous audit findings and remediation status
- Training and awareness programs
- Documentation and record-keeping practices

Provide a compliance rating and prioritized remediation recommendations.",
            OutputTemplate = @"# Compliance Review Report

---

## Review Details
**Organization:** {{ORGANIZATION_NAME}}
**Review Date:** {{REVIEW_DATE}}
**Compliance Officer:** {{COMPLIANCE_OFFICER}}
**Review Scope:** {{REVIEW_SCOPE}}

---

## Regulatory Frameworks
**Applicable Regulations:** {{APPLICABLE_REGULATIONS}}
**Industry Standards:** {{INDUSTRY_STANDARDS}}
**Internal Policies:** {{INTERNAL_POLICIES}}

---

## Compliance Status
**Overall Compliance Score:** {{COMPLIANCE_SCORE}}
**Compliant Controls:** {{COMPLIANT_CONTROLS}}
**Partially Compliant Controls:** {{PARTIALLY_COMPLIANT}}
**Non-Compliant Controls:** {{NON_COMPLIANT_CONTROLS}}

---

## Key Findings
**Critical Findings:** {{CRITICAL_FINDINGS}}
**High Priority Findings:** {{HIGH_PRIORITY_FINDINGS}}
**Medium Priority Findings:** {{MEDIUM_PRIORITY_FINDINGS}}
**Observations:** {{OBSERVATIONS}}

---

## Control Assessment
**Policy Documentation:** {{POLICY_DOCUMENTATION}}
**Procedure Implementation:** {{PROCEDURE_IMPLEMENTATION}}
**Control Effectiveness:** {{CONTROL_EFFECTIVENESS}}
**Segregation of Duties:** {{SEGREGATION_OF_DUTIES}}

---

## Training & Awareness
**Training Programs:** {{TRAINING_PROGRAMS}}
**Completion Rates:** {{COMPLETION_RATES}}
**Awareness Campaigns:** {{AWARENESS_CAMPAIGNS}}

---

## Documentation Review
**Record Keeping:** {{RECORD_KEEPING}}
**Document Retention:** {{DOCUMENT_RETENTION}}
**Audit Trail:** {{AUDIT_TRAIL}}

---

## Previous Audits
**Previous Findings:** {{PREVIOUS_FINDINGS}}
**Remediation Status:** {{REMEDIATION_STATUS}}
**Recurring Issues:** {{RECURRING_ISSUES}}

---

## Compliance Gaps
**Identified Gaps:** {{IDENTIFIED_GAPS}}
**Root Causes:** {{ROOT_CAUSES}}
**Impact Assessment:** {{IMPACT_ASSESSMENT}}

---

## Recommendations
**Immediate Actions:** {{IMMEDIATE_ACTIONS}}
**Remediation Plan:** {{REMEDIATION_PLAN}}
**Timeline:** {{TIMELINE}}
**Resources Required:** {{RESOURCES_REQUIRED}}

---

## Overall Assessment
**Compliance Rating:** {{COMPLIANCE_RATING}}
**Risk Level:** {{RISK_LEVEL}}
**Executive Summary:** {{EXECUTIVE_SUMMARY}}",
            JsonSchema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""ORGANIZATION_NAME"": { ""type"": ""string"" },
    ""REVIEW_DATE"": { ""type"": ""string"", ""format"": ""date"" },
    ""COMPLIANCE_OFFICER"": { ""type"": ""string"" },
    ""REVIEW_SCOPE"": { ""type"": ""string"" },
    ""APPLICABLE_REGULATIONS"": { ""type"": ""string"" },
    ""INDUSTRY_STANDARDS"": { ""type"": ""string"" },
    ""INTERNAL_POLICIES"": { ""type"": ""string"" },
    ""COMPLIANCE_SCORE"": { ""type"": ""string"" },
    ""COMPLIANT_CONTROLS"": { ""type"": ""number"" },
    ""PARTIALLY_COMPLIANT"": { ""type"": ""number"" },
    ""NON_COMPLIANT_CONTROLS"": { ""type"": ""number"" },
    ""CRITICAL_FINDINGS"": { ""type"": ""string"" },
    ""HIGH_PRIORITY_FINDINGS"": { ""type"": ""string"" },
    ""MEDIUM_PRIORITY_FINDINGS"": { ""type"": ""string"" },
    ""OBSERVATIONS"": { ""type"": ""string"" },
    ""POLICY_DOCUMENTATION"": { ""type"": ""string"" },
    ""PROCEDURE_IMPLEMENTATION"": { ""type"": ""string"" },
    ""CONTROL_EFFECTIVENESS"": { ""type"": ""string"" },
    ""SEGREGATION_OF_DUTIES"": { ""type"": ""string"" },
    ""TRAINING_PROGRAMS"": { ""type"": ""string"" },
    ""COMPLETION_RATES"": { ""type"": ""string"" },
    ""AWARENESS_CAMPAIGNS"": { ""type"": ""string"" },
    ""RECORD_KEEPING"": { ""type"": ""string"" },
    ""DOCUMENT_RETENTION"": { ""type"": ""string"" },
    ""AUDIT_TRAIL"": { ""type"": ""string"" },
    ""PREVIOUS_FINDINGS"": { ""type"": ""string"" },
    ""REMEDIATION_STATUS"": { ""type"": ""string"" },
    ""RECURRING_ISSUES"": { ""type"": ""string"" },
    ""IDENTIFIED_GAPS"": { ""type"": ""string"" },
    ""ROOT_CAUSES"": { ""type"": ""string"" },
    ""IMPACT_ASSESSMENT"": { ""type"": ""string"" },
    ""IMMEDIATE_ACTIONS"": { ""type"": ""string"" },
    ""REMEDIATION_PLAN"": { ""type"": ""string"" },
    ""TIMELINE"": { ""type"": ""string"" },
    ""RESOURCES_REQUIRED"": { ""type"": ""string"" },
    ""COMPLIANCE_RATING"": { ""type"": ""string"", ""enum"": [""Fully Compliant"", ""Substantially Compliant"", ""Partially Compliant"", ""Non-Compliant""] },
    ""RISK_LEVEL"": { ""type"": ""string"", ""enum"": [""Low"", ""Medium"", ""High"", ""Critical""] },
    ""EXECUTIVE_SUMMARY"": { ""type"": ""string"" }
  },
  ""required"": [""ORGANIZATION_NAME"", ""REVIEW_DATE"", ""COMPLIANCE_RATING"", ""RISK_LEVEL""]
}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }
    };
}
