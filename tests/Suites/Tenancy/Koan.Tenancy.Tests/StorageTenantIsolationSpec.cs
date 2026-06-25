using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Storage.Abstractions;
using Koan.Storage.Infrastructure;
using Koan.Storage.Model;
using Koan.Tenancy.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// STOR-0011 — the storage blob-key tenant-isolation proof, through a real <c>AddKoan()</c> boot (ARCH-0079) with
/// <c>Koan.Tenancy</c> + the no-Docker Local storage provider. The <c>ScopedStorageService</c> decorator at the
/// <c>IStorageService</c> boundary composes the leading <c>__koan_tenant</c> particle onto every blob op, so two
/// tenants writing the SAME logical key get physically distinct, mutually-invisible blobs. Covers the
/// <c>StorageEntity&lt;T&gt;</c> surface, the type-erased raw <c>IStorageService</c> surface (the fail-safe ambient
/// path), the unscoped fail-closed gate, the <c>[HostScoped]</c> exemption, and the hostile-path-value rejection.
/// (The <c>MediaEntity</c>/presign surface is proven in the S6.SnapVault dogfood — the Local provider has no presign.)
/// </summary>
public sealed class StorageTenantIsolationSpec
{
    private static IReadOnlyDictionary<string, string?> Posture(string p)
        => new Dictionary<string, string?> { ["Koan:Data:Tenancy:Posture"] = p };

    [StorageBinding(Profile = "test", Container = "blobs")]
    public sealed class TenantBlob : StorageEntity<TenantBlob> { }

    [HostScoped]
    [StorageBinding(Profile = "test", Container = "sys")]
    public sealed class SystemBlob : StorageEntity<SystemBlob> { }

    private static Stream Bytes(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    private static async Task<string> ReadAll(Stream s)
    {
        using var r = new StreamReader(s);
        return await r.ReadToEndAsync();
    }

    [Fact(DisplayName = "storage isolation: two tenants writing the SAME blob name get physically distinct, isolated blobs")]
    public async Task Two_tenants_same_name_are_isolated()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"), withLocalStorage: true);
        runtime.ResetEntityCaches();

        using (Tenant.Use("acme")) await TenantBlob.Onboard("photo.jpg", Bytes("ACME-PIXELS"));
        using (Tenant.Use("globex")) await TenantBlob.Onboard("photo.jpg", Bytes("GLOBEX-PIXELS"));

        // Each tenant reads its OWN bytes for the same logical name — physical isolation (acme/photo.jpg vs globex/photo.jpg).
        using (Tenant.Use("acme")) (await TenantBlob.Get("photo.jpg").ReadAllText()).Should().Be("ACME-PIXELS");
        using (Tenant.Use("globex")) (await TenantBlob.Get("photo.jpg").ReadAllText()).Should().Be("GLOBEX-PIXELS");

        // The persisted logical key is unprefixed (STOR-0011 §5: the entity holds the LOGICAL key, never acme/...).
        using (Tenant.Use("acme")) TenantBlob.Get("photo.jpg").Key.Should().Be("photo.jpg");
    }

    [Fact(DisplayName = "storage isolation: a raw IStorageService op (type-erased) is isolated by the ambient tenant (fail-safe)")]
    public async Task Raw_storage_service_is_isolated_by_the_ambient_tenant()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"), withLocalStorage: true);
        runtime.ResetEntityCaches();
        var svc = runtime.Services.GetRequiredService<IStorageService>();

        using (Tenant.Use("acme")) await svc.Put("test", "raw", "r.bin", Bytes("ACME-RAW"), "application/octet-stream");
        using (Tenant.Use("globex")) await svc.Put("test", "raw", "r.bin", Bytes("GLOBEX-RAW"), "application/octet-stream");

        using (Tenant.Use("acme")) (await ReadAll(await svc.Read("test", "raw", "r.bin"))).Should().Be("ACME-RAW");
        using (Tenant.Use("globex")) (await ReadAll(await svc.Read("test", "raw", "r.bin"))).Should().Be("GLOBEX-RAW");
    }

    [Fact(DisplayName = "storage isolation: an unscoped write of a tenant-scoped blob fails closed (Closed posture)")]
    public async Task Unscoped_write_fails_closed()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"), withLocalStorage: true);
        runtime.ResetEntityCaches();

        // No tenant in scope (Test env ⇒ no dev-seed) ⇒ a tenant-scoped blob op must fail closed, not land globally.
        var act = async () => await TenantBlob.Onboard("x.jpg", Bytes("x"));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact(DisplayName = "storage isolation: a [HostScoped] blob is unprefixed and shared across tenants")]
    public async Task HostScoped_blob_is_shared()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"), withLocalStorage: true);
        runtime.ResetEntityCaches();

        await SystemBlob.Onboard("banner.png", Bytes("SHARED"));   // [HostScoped] ⇒ no tenant needed, no prefix
        using (Tenant.Use("acme")) (await SystemBlob.Get("banner.png").ReadAllText()).Should().Be("SHARED");
        using (Tenant.Use("globex")) (await SystemBlob.Get("banner.png").ReadAllText()).Should().Be("SHARED");
    }

    [Fact(DisplayName = "storage isolation: a path-hostile tenant id is rejected by the sanitizing formatter")]
    public async Task Hostile_tenant_id_is_rejected()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"), withLocalStorage: true);
        runtime.ResetEntityCaches();

        // A tenant whose id would traverse out of its prefix ("../globex/...") cannot become a blob path segment.
        using (Tenant.Use("../evil"))
        {
            var act = async () => await TenantBlob.Onboard("x.jpg", Bytes("x"));
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
