using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

/// <summary>
/// Organization-wide context injected into document processing prompts.
/// </summary>
public sealed class OrganizationProfile : Entity<OrganizationProfile>
{
    public string Name { get; set; } = string.Empty;
    public string ScopeClassification { get; set; } = string.Empty;
    public string RegulatoryRegime { get; set; } = string.Empty;
    public string LineOfBusiness { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public Dictionary<string, List<string>> PrimaryStakeholders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public void UpsertStakeholder(string role, IEnumerable<string> contacts)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return;
        }

        var roleKey = role.Trim();
        var values = contacts?.Where(contact => !string.IsNullOrWhiteSpace(contact))
            .Select(contact => contact.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        PrimaryStakeholders[roleKey] = values;
    }

    public void RemoveStakeholderRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return;
        }

        PrimaryStakeholders.Remove(role.Trim());
    }

    public async Task<List<DocumentPipeline>> LoadPipelinesAsync(CancellationToken ct = default)
    {
        var pipelines = await DocumentPipeline.Query(p => p.OrganizationProfileId == Id, ct).ConfigureAwait(false);
        return pipelines.ToList();
    }
}
