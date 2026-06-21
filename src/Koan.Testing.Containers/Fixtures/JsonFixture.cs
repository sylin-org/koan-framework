using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Koan.Testing.Containers;

/// <summary>
/// ARCH-0091 dockerless fixture for the JSON file adapter. There is no container — the "backing store"
/// is a temp directory tree under <c>Path.GetTempPath()</c>, removed on dispose. Unlike the container
/// fixtures (which share one backing store per assembly and isolate by partition), this fixture hands
/// each boot a FRESH subdirectory via <see cref="SettingsForBoot"/>: JSON specs assert over the whole
/// on-disk store (e.g. "every json file is <c>[]</c> after Clear"), which a shared root — accumulating
/// every test's files — would break. A fresh per-boot directory restores the legacy
/// <c>JsonConnectorFixture</c> one-store-per-test isolation at zero cost. <c>AddKoan()</c> registers the
/// default <c>IStorageNameResolver</c> (TryAddSingleton), so no extra service wiring is needed.
/// </summary>
public sealed class JsonFixture : KoanContainerFixture
{
    private string? _baseRoot;
    private string? _currentBootRoot;

    public override string Engine => "json";
    protected override string Adapter => "json";

    /// <summary>The JSON store directory for the CURRENT boot (a fresh per-boot subdirectory under the base).</summary>
    public string RootPath => _currentBootRoot
        ?? throw new InvalidOperationException("No active boot — call BootAsync() before reading RootPath.");

    protected override Task<string> StartContainerAsync()
    {
        _baseRoot = Path.Combine(Path.GetTempPath(), "Koan-JsonConnector", Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(_baseRoot);
        // Empty connection string → base omits Sources:Default:ConnectionString; DirectoryPath drives the adapter.
        return Task.FromResult(string.Empty);
    }

    /// <summary>
    /// A fresh JSON store directory per boot. File-global assertions must see only the current test's
    /// data. Sequential execution (<c>DisableTestParallelization</c>) makes the single
    /// <see cref="_currentBootRoot"/> field safe; all per-boot dirs live under <see cref="_baseRoot"/>
    /// and are removed wholesale on dispose.
    /// </summary>
    public override IReadOnlyDictionary<string, string?> SettingsForBoot()
    {
        _currentBootRoot = Path.Combine(_baseRoot!, Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(_currentBootRoot);
        return new Dictionary<string, string?>(Settings, StringComparer.Ordinal)
        {
            ["Koan:Data:Json:DirectoryPath"] = _currentBootRoot,
        };
    }

    protected override ValueTask StopContainerAsync()
    {
        if (_baseRoot is not null)
        {
            try { if (Directory.Exists(_baseRoot)) Directory.Delete(_baseRoot, recursive: true); }
            catch { /* best-effort temp cleanup */ }
        }
        return ValueTask.CompletedTask;
    }

    // The static Settings still need a DirectoryPath (the base shape); SettingsForBoot refines it per boot.
    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings(string connectionString) => new[]
    {
        new KeyValuePair<string, string?>("Koan:Data:Json:DirectoryPath", _baseRoot),
    };
}
