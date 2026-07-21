namespace Koan.Tenancy.Web.Controllers;

public sealed record TenantSummary(
    string Id,
    string Name,
    string? Code,
    int SeatCount);

public sealed record TenantRoster(
    string Posture,
    string Operator,
    IReadOnlyList<TenantSummary> Tenants);

public sealed record TenantDetail(
    TenantRecord Tenant,
    IReadOnlyList<Membership> Memberships);

public sealed record CreateTenantRequest(string Name, string? Code);

public sealed record RenameTenantRequest(string Name);

public sealed record GrantMembershipRequest(string IdentityId, IReadOnlyList<string>? Roles);
