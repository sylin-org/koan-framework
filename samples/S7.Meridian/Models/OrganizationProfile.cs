using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

/// <summary>
/// Template defining fields that should be extracted from ALL pipelines.
/// Only one OrganizationProfile can be active at a time.
/// </summary>
public sealed class OrganizationProfile : Entity<OrganizationProfile>
{
    /// <summary>User-friendly label (e.g., "Geisinger Healthcare").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Only one profile can be active at a time.</summary>
    public bool Active { get; set; }

    /// <summary>Field definitions to extract from all documents.</summary>
    public List<OrganizationFieldDefinition> Fields { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Adds or updates a field definition.</summary>
    public void UpsertField(string fieldName, string? description = null, IEnumerable<string>? examples = null)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        var existing = Fields.FirstOrDefault(f => f.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Description = description;
            existing.Examples = examples?.ToList() ?? new List<string>();
        }
        else
        {
            Fields.Add(new OrganizationFieldDefinition
            {
                FieldName = fieldName,
                Description = description,
                Examples = examples?.ToList() ?? new List<string>(),
                DisplayOrder = Fields.Count
            });
        }
    }

    /// <summary>Removes a field definition by name.</summary>
    public void RemoveField(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        Fields.RemoveAll(f => f.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        ReorderFields();
    }

    /// <summary>Reorders field display order after modifications.</summary>
    public void ReorderFields()
    {
        for (int i = 0; i < Fields.Count; i++)
        {
            Fields[i].DisplayOrder = i;
        }
    }

    /// <summary>Gets the currently active OrganizationProfile, if any.</summary>
    public static async Task<OrganizationProfile?> GetActiveAsync(CancellationToken ct = default)
    {
        var profiles = await Query(p => p.Active, ct);
        return profiles.FirstOrDefault();
    }

    /// <summary>
    /// Activates this profile and deactivates all others.
    /// Ensures only one profile is active at a time.
    /// </summary>
    public async Task ActivateAsync(CancellationToken ct = default)
    {
        // Deactivate all other profiles
        var allProfiles = await All(ct);
        foreach (var profile in allProfiles.Where(p => p.Id != Id && p.Active))
        {
            profile.Active = false;
            profile.UpdatedAt = DateTime.UtcNow;
            await profile.Save(ct);
        }

        // Activate this profile
        Active = true;
        UpdatedAt = DateTime.UtcNow;
        await this.Save(ct);
    }

    /// <summary>Deactivates this profile.</summary>
    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        Active = false;
        UpdatedAt = DateTime.UtcNow;
        await this.Save(ct);
    }
}
