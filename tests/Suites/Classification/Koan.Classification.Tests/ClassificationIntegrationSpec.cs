using System.Text;
using AwesomeAssertions;
using Koan.Cache.Abstractions.Policies;
using Koan.Classification.Crypto;
using Koan.Classification.Tests.Support;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Core.Diagnostics;
using Koan.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Classification.Tests;

/// <summary>
/// ARCH-0098 phase 3 — the flagship classification round-trip through a real <c>AddKoan()</c> boot (ARCH-0079) on
/// the no-Docker SQLite adapter with <c>Koan.Classification</c> referenced (auto-registrar discovered, transform
/// wired). Proves: at-rest is ciphertext (raw read + lost-custody failure), every read path decrypts (Get, Get-many,
/// All/Query, QueryRaw), every write path encrypts (Save, UpsertMany, Batch — Blocker 1), the caller keeps
/// plaintext (clone-then-encrypt), non-classified fields are untouched, and classified cache entries are excluded.
/// </summary>
public sealed class ClassificationIntegrationSpec
{
    // A fresh storage partition per test isolates each test's rows from others sharing the engine (ARCH-0091).
    private static IDisposable Isolate() => EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

    [HostScoped]
    public sealed class Patient : Entity<Patient>
    {
        [Pii] public string Name { get; set; } = "";
        [Phi] public string? Diagnosis { get; set; }
        public string Ward { get; set; } = "";
    }

    [HostScoped, Cacheable(300)]
    public sealed class CachedPatient : Entity<CachedPatient>
    {
        [Pii] public string Name { get; set; } = "";
        public string Ward { get; set; } = "";
    }

    public sealed class TenantPatient : Entity<TenantPatient>
    {
        [Pii] public string Name { get; set; } = "";
    }

    private sealed class RecordingKeyProvider : IClassificationKeyProvider
    {
        private readonly Dictionary<string, ClassificationDataKey> _byScope = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ClassificationDataKey> _byId = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Scopes => _byScope.Keys;

        public ClassificationDataKey GetActiveKey(string scope)
        {
            if (_byScope.TryGetValue(scope, out var existing)) return existing;
            var created = new ClassificationDataKey(Guid.NewGuid().ToString("N"),
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            _byScope.Add(scope, created);
            _byId.Add(created.KeyId, created);
            return created;
        }

        public ClassificationDataKey GetForDecrypt(string keyId)
            => _byId.TryGetValue(keyId, out var key)
                ? key
                : throw new ClassificationKeyUnavailableException(keyId);
    }

    /// <summary>Reads every user table's Json column directly, bypassing the facade — the at-rest truth.</summary>
    private static string ReadRawAtRest(string dbPath)
    {
        // A plain (read-write) connection: ReadOnly fails to open a WAL database (it cannot create the -shm/-wal it
        // needs), and SQLite permits concurrent readers alongside the host's connection.
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var tables = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tables.Add(r.GetString(0));
        }

        var sb = new StringBuilder();
        foreach (var t in tables)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT Json FROM \"{t}\"";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (!r.IsDBNull(0)) sb.AppendLine(r.GetString(0));
            }
            catch (SqliteException)
            {
                // a table without a Json column — skip
            }
        }
        return sb.ToString();
    }

    [Fact(DisplayName = "round-trips through the store: classified fields are restored to plaintext on read")]
    public async Task Round_trips_through_the_store()
    {
        await using var fx = await ClassificationRuntimeFixture.CreateAsync();
        fx.ResetEntityCaches();
        using var _iso = Isolate();

        var p = await new Patient { Name = "Ada Lovelace", Diagnosis = "influenza", Ward = "3-West" }.Save();

        var loaded = await Patient.Get(p.Id);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Ada Lovelace");
        loaded.Diagnosis.Should().Be("influenza");
        loaded.Ward.Should().Be("3-West");
    }

    [Fact(DisplayName = "the caller's instance keeps plaintext after Save (clone-then-encrypt)")]
    public async Task Save_does_not_corrupt_the_callers_instance()
    {
        await using var fx = await ClassificationRuntimeFixture.CreateAsync();
        fx.ResetEntityCaches();
        using var _iso = Isolate();

        var p = new Patient { Name = "Grace Hopper" };
        await p.Save();

        p.Name.Should().Be("Grace Hopper");   // the persisted clone was encrypted; the original is untouched
    }

    [Fact(DisplayName = "at rest is ciphertext: the raw stored bytes hold an envelope, never the plaintext")]
    public async Task At_rest_is_ciphertext_raw_read()
    {
        await using var fx = await ClassificationRuntimeFixture.CreateAsync();
        fx.ResetEntityCaches();
        // No partition: the fresh per-test db is the isolation, and the rows land in the default table the raw read scans.
        await new Patient { Name = "Alan Turing", Diagnosis = "secret-dx", Ward = "B-12" }.Save();

        var atRest = ReadRawAtRest(fx.DbPath);
        atRest.Should().Contain(FieldCipherEnvelope.Magic);   // an envelope is stored
        atRest.Should().NotContain("Alan Turing");            // the PII plaintext is NOT at rest
        atRest.Should().NotContain("secret-dx");
        atRest.Should().Contain("B-12");                      // a non-classified field is stored as-is
    }

    [Fact(DisplayName = "lost key custody fails loudly rather than returning ciphertext or hiding the failure")]
    public async Task Lost_key_custody_fails_loudly()
    {
        await using var fx = await ClassificationRuntimeFixture.CreateAsync();
        fx.ResetEntityCaches();
        using var _iso = Isolate();

        var p = await new Patient { Name = "to be forgotten", Ward = "A-1" }.Save();

        fx.Services.GetRequiredService<IClassificationKeyProvider>().Should()
            .BeOfType<EphemeralClassificationKeyProvider>().Which.Dispose();

        var read = async () => await Patient.Get(p.Id);
        await read.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact(DisplayName = "every read path decrypts: Get, Get-many, All/Query, QueryRaw")]
    public async Task Every_read_path_decrypts()
    {
        await using var fx = await ClassificationRuntimeFixture.CreateAsync();
        fx.ResetEntityCaches();
        using var _iso = Isolate();

        var a = await new Patient { Name = "Edsger", Ward = "shortest-path" }.Save();
        var b = await new Patient { Name = "Donald", Ward = "concrete" }.Save();

        // Get (single)
        (await Patient.Get(a.Id))!.Name.Should().Be("Edsger");
        // Get (many)
        (await Patient.Get(new[] { a.Id, b.Id })).Select(p => p!.Name).Should().BeEquivalentTo("Edsger", "Donald");
        // All / structured Query
        (await Patient.All()).Select(p => p.Name).Should().BeEquivalentTo("Edsger", "Donald");
        (await Patient.Query(p => p.Ward == "concrete")).Select(p => p.Name).Should().Equal("Donald");
        // QueryRaw — the values come back from raw SQL still protected; the reverse restores them
        (await Patient.QueryRaw("SELECT Id, Json FROM Patient")).Select(p => p.Name)
            .Should().BeEquivalentTo("Edsger", "Donald");
    }

    [Fact(DisplayName = "every write path encrypts at rest: Save, UpsertMany, and Batch (Blocker 1)")]
    public async Task Every_write_path_encrypts_at_rest()
    {
        await using var fx = await ClassificationRuntimeFixture.CreateAsync();
        fx.ResetEntityCaches();
        // No partition: the fresh per-test db isolates, and rows land in the default table the raw read scans.

        // Save (Upsert)
        await new Patient { Name = "SaveSecret" }.Save();
        // UpsertMany
        await Patient.UpsertMany(new[] { new Patient { Name = "ManySecretA" }, new Patient { Name = "ManySecretB" } });
        // Batch (the path that excludes [Timestamp] — must NOT exclude encryption)
        var batch = Patient.Batch();
        batch.Add(new Patient { Name = "BatchSecret1" });
        batch.Add(new Patient { Name = "BatchSecret2" });
        await batch.Save();

        var atRest = ReadRawAtRest(fx.DbPath);
        foreach (var plaintext in new[] { "SaveSecret", "ManySecretA", "ManySecretB", "BatchSecret1", "BatchSecret2" })
            atRest.Should().NotContain(plaintext);   // none of the write paths persisted plaintext

        // And every one reads back as plaintext.
        (await Patient.All()).Select(p => p.Name).Should().BeEquivalentTo(
            "SaveSecret", "ManySecretA", "ManySecretB", "BatchSecret1", "BatchSecret2");
    }

    [Fact(DisplayName = "a [Cacheable] classified entity is excluded from the cache (no stale plaintext served after a shred)")]
    public async Task Cacheable_classified_entity_is_excluded_from_the_cache()
    {
        await using var fx = await ClassificationRuntimeFixture.CreateAsync();
        fx.ResetEntityCaches();

        var p = await new CachedPatient { Name = "cached-secret", Ward = "C-9" }.Save();
        (await CachedPatient.Get(p.Id))!.Name.Should().Be("cached-secret");   // populates the cache iff NOT excluded

        // Lose local key custody. A cached plaintext copy would survive and return; an excluded Entity re-enters Data
        // and fails at the key boundary.
        fx.Services.GetRequiredService<IClassificationKeyProvider>().Should()
            .BeOfType<EphemeralClassificationKeyProvider>().Which.Dispose();

        // If the entity were cached, this would be a cache HIT returning the stale PLAINTEXT — the L2 leak. Because it
        // is excluded, the read goes to the store and fails at the missing key boundary.
        var read = async () => await CachedPatient.Get(p.Id);
        await read.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact(DisplayName = "an empty store and unclassified data are unaffected (no false positives)")]
    public async Task Non_classified_fields_round_trip_untouched()
    {
        await using var fx = await ClassificationRuntimeFixture.CreateAsync();
        fx.ResetEntityCaches();
        using var _iso = Isolate();

        var p = await new Patient { Name = "x", Ward = "plain-ward", Diagnosis = null }.Save();
        var loaded = await Patient.Get(p.Id);

        loaded!.Ward.Should().Be("plain-ward");   // unclassified — never transformed
        loaded.Diagnosis.Should().BeNull();        // null classified — stays null
    }

    [Fact(DisplayName = "Tenancy automatically partitions Classification key scope without Classification configuration")]
    public async Task Tenancy_automatically_partitions_key_scope()
    {
        var keys = new RecordingKeyProvider();
        await using var fx = await ClassificationRuntimeFixture.CreateAsync(
            configureServices: services => services.AddSingleton<IClassificationKeyProvider>(keys));
        fx.ResetEntityCaches();
        string tenantAId;
        string tenantBId;

        using (Tenant.Use("tenant-a"))
            tenantAId = (await new TenantPatient { Name = "Ada" }.Save()).Id;

        using (Tenant.Use("tenant-b"))
            tenantBId = (await new TenantPatient { Name = "Grace" }.Save()).Id;

        keys.Scopes.Should().HaveCount(2);
        keys.Scopes.Should().OnlyContain(scope => scope.StartsWith("seg:", StringComparison.Ordinal));

        using (Tenant.Use("tenant-a"))
        {
            (await TenantPatient.Get(tenantAId))!.Name.Should().Be("Ada");
            (await TenantPatient.Get(tenantBId)).Should().BeNull();
        }
        using (Tenant.Use("tenant-b"))
        {
            (await TenantPatient.Get(tenantBId))!.Name.Should().Be("Grace");
            (await TenantPatient.Get(tenantAId)).Should().BeNull();
        }
    }

    [Fact(DisplayName = "startup facts state the active guarantee, provider, scope ownership, and exclusions")]
    public async Task Startup_facts_are_exact_and_value_free()
    {
        await using var fx = await ClassificationRuntimeFixture.CreateAsync();

        var fact = fx.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts.Should()
            .ContainSingle(item => item.Code == Infrastructure.Constants.Diagnostics.CapabilityCode)
            .Which;
        fact.Subject.Should().Be("classification:field-at-rest");
        fact.ReasonCode.Should().Be("compiled-field-transform");
        fact.Summary.Should().Contain("AES-256-GCM")
            .And.Contain(typeof(EphemeralClassificationKeyProvider).FullName!)
            .And.Contain("compiled segmentation")
            .And.Contain("No search, masking, backfill, redaction, or erasure")
            .And.NotContain("tenant-a");
    }
}
