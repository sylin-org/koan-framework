using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Storage.Abstractions;
using Koan.Storage.Keys;
using Koan.Tenancy.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// NK-084 through a real <c>AddKoan()</c> boot with row-scoped tenancy on SQLite. A backup archive is stored host-wide,
/// and its restore writes under the current tenant, so an archive taken under one tenant restored under another copied
/// its rows into that tenant. The archive records the scope it was taken in, and a restore elsewhere is refused unless
/// it is a deliberate administrative copy.
/// </summary>
public sealed class TenantBackupSpec
{
    private const string Profile = "test";

    public sealed class Invoice : Entity<Invoice>
    {
        public string Title { get; set; } = "";
    }

    [Fact(DisplayName = "NK-084: an archive restores only under the tenant it was taken in, unless the restore opts across")]
    public async Task Archive_restores_only_under_the_tenant_it_was_taken_in()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(
            extraSettings: new Dictionary<string, string?> { ["Koan:Tenancy:Posture"] = "Closed" },
            withLocalStorage: true);
        runtime.ResetEntityCaches();
        using var services = runtime.Services.CreateScope();
        var backups = services.ServiceProvider.GetRequiredService<IBackupService>();

        BackupReceipt archive;
        using (Tenant.Use("acme"))
        {
            await new Invoice { Id = "inv-1", Title = "acme" }.Save();
            archive = await backups.Create<Invoice, string>("acme-invoices", new BackupRequest { StorageProfile = Profile });
            await Invoice.Remove("inv-1");
        }

        using (Tenant.Use("globex"))
        {
            var crossing = await Record.ExceptionAsync(() => backups.Restore<Invoice, string>(
                archive.StorageKey, new RestoreRequest { StorageProfile = Profile }));
            crossing.Should().BeOfType<InvalidOperationException>()
                .Which.Message.Should().Contain("different tenant").And.Contain("AcrossScopes");
            (await Invoice.All()).Should().BeEmpty("nothing was restored into globex");
        }

        using (Tenant.Use("acme"))
        {
            (await backups.Restore<Invoice, string>(archive.StorageKey, new RestoreRequest { StorageProfile = Profile }))
                .RecordCount.Should().Be(1);
            (await Invoice.Get("inv-1"))!.Title.Should().Be("acme");
            await Invoice.Remove("inv-1");   // one shared table: the identity must be free for the copy below
        }

        using (Tenant.Use("globex"))
        {
            (await backups.Restore<Invoice, string>(
                    archive.StorageKey, new RestoreRequest { StorageProfile = Profile, AcrossScopes = true }))
                .RecordCount.Should().Be(1, "an administrative copy is still possible, on purpose");
            (await Invoice.Get("inv-1")).Should().NotBeNull();
        }
    }

    [Fact(DisplayName = "NK-084: a format 1 archive, which recorded no tenant, restores under a tenant only with the override")]
    public async Task Legacy_archive_restores_under_a_tenant_only_with_the_override()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(
            extraSettings: new Dictionary<string, string?> { ["Koan:Tenancy:Posture"] = "Closed" },
            withLocalStorage: true);
        runtime.ResetEntityCaches();
        using var services = runtime.Services.CreateScope();
        var backups = services.ServiceProvider.GetRequiredService<IBackupService>();

        using (Tenant.Use("acme"))
        {
            await new Invoice { Id = "old-1", Title = "acme" }.Save();
            var archive = await backups.Create<Invoice, string>("legacy", new BackupRequest { StorageProfile = Profile });
            await Invoice.Remove("old-1");
            await MakeFormatOne(services.ServiceProvider, archive);

            var refused = await Record.ExceptionAsync(() => backups.Restore<Invoice, string>(
                archive.StorageKey, new RestoreRequest { StorageProfile = Profile }));
            refused.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("format 1");
            (await Invoice.All()).Should().BeEmpty();

            (await backups.Restore<Invoice, string>(
                    archive.StorageKey, new RestoreRequest { StorageProfile = Profile, AcrossScopes = true }))
                .RecordCount.Should().Be(1);
        }
    }

    /// <summary>Rewrites a published archive's manifest as format 1 wrote it: no scope.</summary>
    private static async Task MakeFormatOne(IServiceProvider services, BackupReceipt archive)
    {
        var storage = services.GetRequiredService<IStorageService>();
        using var host = StorageScope.HostScoped();
        var buffer = new MemoryStream();
        await using (var published = await storage.Read(Profile, "backups", archive.StorageKey))
            await published.CopyToAsync(buffer);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("manifest.json")!;
            JsonObject manifest;
            using (var input = entry.Open()) manifest = JsonNode.Parse(input)!.AsObject();
            manifest["formatVersion"] = 1;
            manifest.Remove("scope");
            entry.Delete();
            using var output = zip.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(output, manifest);
        }
        buffer.Position = 0;
        await storage.Put(Profile, "backups", archive.StorageKey, buffer, "application/zip");
    }
}
