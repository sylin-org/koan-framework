using System.ComponentModel.DataAnnotations;
using Koan.Canon.Domain.Model;

namespace S8.Canon.Domain;

/// <summary>
/// Canonical customer entity demonstrating Canon runtime pipeline processing.
/// Raw customer data flows through validation → enrichment → canonical storage.
/// </summary>
public class Customer : CanonEntity<Customer>
{
    /// <summary>
    /// Customer's email address (required, normalized to lowercase).
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Customer's phone number (optional, normalized to E.164 format).
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Customer's first name.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Customer's last name.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Computed display name (enriched during canonization).
    /// Format: "FirstName LastName" or email prefix if names missing.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Computed account tier based on customer attributes (enriched during canonization).
    /// Values: "Premium", "Standard", "Basic"
    /// </summary>
    public string AccountTier { get; set; } = "Basic";

    /// <summary>
    /// Customer's country code (ISO 3166-1 alpha-2).
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Customer's preferred language (ISO 639-1).
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Date when customer record was first created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Date when customer record was last updated.
    /// </summary>
    [Timestamp]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
