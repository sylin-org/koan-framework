using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Tests.Data.Core.Support;

namespace Koan.Tests.Data.Core.Specs.Hydration;

/// <summary>
/// PMC-061. An Entity whose constructor seeds an audit collection (the exact shape of
/// <c>CanonStage&lt;TModel&gt;</c>'s "Stage created" transition) must keep exactly ONE copy of that
/// seed across create → persist → reload, no matter how many save/reload cycles it goes through.
/// Before the fix, hydration deserialized with Newtonsoft's default <c>ObjectCreationHandling.Auto</c>,
/// which populates the constructor-seeded list by ADDING the stored history on top — so every
/// reload duplicated the constructor artifact (observed 3–5 "Stage created" entries per receipt
/// in the blind capability run). The fix makes hydration store-authoritative:
/// <c>ObjectCreationHandling.Replace</c> in both the relational entity codec and the shared
/// document materializer. This spec pins the property on the real SQLite adapter.
/// </summary>
public sealed class CtorSeededCollectionRoundTripSpec
{
    // Same structural shape as CanonStage<TModel>: constructor seeds an audit entry.
    [DataAdapter("sqlite")]
    public sealed class SeededRow : Entity<SeededRow>
    {
        public SeededRow() => Audit.Add("row created");

        public List<string> Audit { get; set; } = new();
        public string? Label { get; set; }
    }

    [Fact]
    public async Task Constructor_seeded_entries_are_never_duplicated_by_reload_cycles()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(includeSqlite: true);
        runtime.BindHost();

        var row = new SeededRow { Label = "first" };
        row.Audit.Should().ContainSingle(e => e == "row created", "the constructor seeds exactly once at creation");

        await row.Save();
        for (var cycle = 1; cycle <= 3; cycle++)
        {
            var reloaded = await SeededRow.Get(row.Id);
            reloaded.Should().NotBeNull();
            reloaded!.Audit.Count(e => e == "row created").Should().Be(1,
                $"hydration cycle {cycle} must be store-authoritative: one seed, never a duplicate");
            // Mutate + persist so the next cycle reloads what THIS cycle wrote — the growth-per-cycle shape
            // the field incident exhibited.
            reloaded.Label = $"cycle-{cycle}";
            await reloaded.Save();
        }
    }
}
