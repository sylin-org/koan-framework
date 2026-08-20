using AwesomeAssertions;
using Koan.Data.Core.Polymorphism;
using Koan.Data.Core.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Entity;

/// <summary>
/// What Koan writes, Koan reads back. The asymmetric case is state an Entity guards behind a non-public setter:
/// Json.NET serializes it happily and then refuses to fill it, so the value reaches storage and is silently
/// replaced by a default on the way back — a write that reported success and a read that quietly lost it.
///
/// <para>This is not hypothetical. The Canon pillar keeps aggregation ownership that way, so a second arrival for
/// an already-known key read back an index with no owner, minted a fresh canonical record, and the pillar's whole
/// promise — messy arrivals converge — failed on the two-arrival case it exists for.</para>
/// </summary>
public sealed class EntityRoundTripSymmetrySpec
{
    private sealed class Guarded : Entity<Guarded>
    {
        public string Label { get; set; } = "";
        public string Owner { get; private set; } = "";
        public int Revision { get; private set; }
        public string? Note { get; internal set; }

        // The only door domain code may use; persistence must not need it.
        public void Claim(string owner, int revision, string? note)
        {
            Owner = owner;
            Revision = revision;
            Note = note;
        }

        public string Computed => $"{Label}:{Owner}";
    }

    private static readonly JsonSerializerSettings Settings =
        EntityJsonSerialization.Apply(new JsonSerializerSettings());

    [Fact(DisplayName = "state behind a non-public setter survives the round trip")]
    public void Guarded_state_round_trips()
    {
        var entity = new Guarded { Id = "guarded-1", Label = "ledger" };
        entity.Claim("canonical-42", 7, "held");

        var restored = JsonConvert.DeserializeObject<Guarded>(
            JsonConvert.SerializeObject(entity, Settings), Settings)!;

        restored.Owner.Should().Be("canonical-42", "persistence restores what persistence wrote");
        restored.Revision.Should().Be(7);
        restored.Note.Should().Be("held");
        restored.Label.Should().Be("ledger");
        restored.Id.Should().Be("guarded-1");
    }

    [Fact(DisplayName = "a computed property is written for readers and never read back")]
    public void Computed_properties_stay_read_only()
    {
        var entity = new Guarded { Id = "guarded-2", Label = "ledger" };
        entity.Claim("canonical-42", 1, null);

        var document = JObject.Parse(JsonConvert.SerializeObject(entity, Settings));
        document.Property("computed", System.StringComparison.OrdinalIgnoreCase)
            .Should().NotBeNull("a computed property is still useful to whoever reads the document");

        // It has no setter, so there is nothing to restore and nothing to lose: it is derived again on read.
        document["computed"] = "tampered";
        var restored = JsonConvert.DeserializeObject<Guarded>(document.ToString(), Settings)!;
        restored.Computed.Should().Be("ledger:canonical-42");
    }
}
