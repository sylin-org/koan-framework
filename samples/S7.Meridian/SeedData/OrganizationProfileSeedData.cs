using Koan.Samples.Meridian.Models;

namespace Koan.Samples.Meridian.SeedData;

/// <summary>
/// Seed data for default OrganizationProfile templates.
/// These define fields that should be extracted from ALL documents when active.
/// </summary>
public static class OrganizationProfileSeedData
{
    public static OrganizationProfile[] GetProfiles() => new[]
    {
        new OrganizationProfile
        {
            Id = "CorporateOrganization",
            Name = "Corporate Organization",
            Active = true, // Default active profile
            Fields = new List<OrganizationFieldDefinition>
            {
                new OrganizationFieldDefinition
                {
                    FieldName = "RegulatoryRegime",
                    Description = "Compliance frameworks and regulatory standards that apply to the organization",
                    Examples = new List<string> { "HIPAA", "SOC 2", "GDPR", "ISO 27001", "PCI-DSS" },
                    DisplayOrder = 0
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "Department",
                    Description = "Business unit or department responsible for the information",
                    Examples = new List<string> { "Legal", "Engineering", "Operations", "Finance", "HR" },
                    DisplayOrder = 1
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "PrimaryStakeholders",
                    Description = "Key contacts and stakeholders by role",
                    Examples = new List<string> { "Legal: Jane Doe", "Compliance Officer: John Smith", "Security Lead: Alex Johnson" },
                    DisplayOrder = 2
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "DataClassification",
                    Description = "Sensitivity level of information contained in the document",
                    Examples = new List<string> { "Public", "Internal", "Confidential", "Restricted", "Highly Confidential" },
                    DisplayOrder = 3
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "ComplianceOfficer",
                    Description = "Name and contact of the compliance officer responsible for this area",
                    Examples = new List<string> { "Sarah Martinez, sarah.martinez@example.com", "Compliance Department" },
                    DisplayOrder = 4
                }
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        },

        new OrganizationProfile
        {
            Id = "OpensourceCollective",
            Name = "Open Source Collective",
            Active = false,
            Fields = new List<OrganizationFieldDefinition>
            {
                new OrganizationFieldDefinition
                {
                    FieldName = "License",
                    Description = "Open source license under which the project or content is distributed",
                    Examples = new List<string> { "MIT", "Apache 2.0", "GPL-3.0", "BSD-3-Clause", "Creative Commons BY-SA" },
                    DisplayOrder = 0
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "Maintainers",
                    Description = "Primary maintainers or core contributors of the project",
                    Examples = new List<string> { "@username", "Jane Doe (@janedoe)", "Core Team" },
                    DisplayOrder = 1
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "Repository",
                    Description = "Source code repository or project home",
                    Examples = new List<string> { "https://github.com/org/project", "GitLab: org/repo", "Bitbucket" },
                    DisplayOrder = 2
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "CommunityGuidelines",
                    Description = "Code of conduct or community participation rules",
                    Examples = new List<string> { "Contributor Covenant", "Custom CoC", "See CONTRIBUTING.md" },
                    DisplayOrder = 3
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "Sponsors",
                    Description = "Organizations or individuals sponsoring the project",
                    Examples = new List<string> { "GitHub Sponsors", "OpenCollective", "Patreon", "Corporate Sponsor: Acme Inc" },
                    DisplayOrder = 4
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "ContributionProcess",
                    Description = "How to contribute code, documentation, or resources",
                    Examples = new List<string> { "Fork and PR", "Issue first", "CLA required", "Open contribution" },
                    DisplayOrder = 5
                }
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        },

        new OrganizationProfile
        {
            Id = "GovernmentAgency",
            Name = "Government Agency",
            Active = false,
            Fields = new List<OrganizationFieldDefinition>
            {
                new OrganizationFieldDefinition
                {
                    FieldName = "Jurisdiction",
                    Description = "Level of government authority",
                    Examples = new List<string> { "Federal", "State", "County", "Municipal", "Tribal" },
                    DisplayOrder = 0
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "AuthorizingStatute",
                    Description = "Legal authority or statute authorizing the agency's actions",
                    Examples = new List<string> { "USC Title 5", "State Code Section 123", "Executive Order 12345" },
                    DisplayOrder = 1
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "OversightBody",
                    Description = "Legislative or executive body providing oversight",
                    Examples = new List<string> { "Congressional Committee", "Governor's Office", "Inspector General", "Ombudsman" },
                    DisplayOrder = 2
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "PublicContactPoint",
                    Description = "Official public contact information",
                    Examples = new List<string> { "Public Affairs Office", "Citizen Services Hotline", "info@agency.gov" },
                    DisplayOrder = 3
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "ClassificationLevel",
                    Description = "Security classification of the document",
                    Examples = new List<string> { "Unclassified", "For Official Use Only", "Confidential", "Secret", "Top Secret" },
                    DisplayOrder = 4
                },
                new OrganizationFieldDefinition
                {
                    FieldName = "RetentionSchedule",
                    Description = "Records retention requirement per agency policy",
                    Examples = new List<string> { "7 years", "Permanent", "3 years after closure", "As per NARA schedule" },
                    DisplayOrder = 5
                }
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }
    };
}
