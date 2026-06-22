using System;
using System.Linq;
using AwesomeAssertions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Pipeline;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

/// <summary>
/// ARCH-0098 phase 1 — the classification FACTS foundation (no crypto): the <c>[Classified]</c>/<c>[Pii]</c>… fact
/// family, the per-type <see cref="ClassifiedPropertyBag"/> scan + Expression-compiled round-trip accessors, and the
/// <see cref="ClassifiedFieldRegistry"/> Type-plane memo + <c>IsEmpty</c> off-gate. Pins: attribute discovery + sugar
/// category mapping, the Searchable opt-in, the round-trip getter/setter, the round-trip-requires-read+write rule,
/// off = no-scan byte-identical, per-type memoization, and the <c>ClassificationCategory</c> value semantics.
/// </summary>
[Collection("classified-field-registry")]   // serialize: the registry is process-global static state
public sealed class ClassifiedFieldFactsSpec : IDisposable
{
    public ClassifiedFieldFactsSpec() => ClassifiedFieldRegistry.Reset();
    public void Dispose() => ClassifiedFieldRegistry.Reset();

    private sealed class Patient
    {
        public string Id { get; set; } = "";
        [Pii] public string Name { get; set; } = "";
        [Phi] public string Diagnosis { get; set; } = "";
        [Pii(Searchable = true)] public string Email { get; set; } = "";
        [Secret] public string ApiKey { get; set; } = "";
        [Classified("trade-secret")] public string Formula { get; set; } = "";
        public string Plain { get; set; } = "";                         // unclassified
        [Pii] public string Computed { get; } = "read-only";            // get-only → skipped (cannot round-trip)
    }

    private sealed class Unclassified
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    // ── ClassificationCategory value semantics ────────────────────────────────────────────────

    [Fact]
    public void Category_wellknown_tokens_are_stable()
    {
        ClassificationCategory.Pii.Name.Should().Be("pii");
        ClassificationCategory.Phi.Name.Should().Be("phi");
        ClassificationCategory.Pci.Name.Should().Be("pci");
        ClassificationCategory.Secret.Name.Should().Be("secret");
    }

    [Fact]
    public void Category_equality_is_by_name()
    {
        new ClassificationCategory("pii").Should().Be(ClassificationCategory.Pii);
        new ClassificationCategory("trade-secret").Should().NotBe(ClassificationCategory.Pii);
        ClassificationCategory.Pii.ToString().Should().Be("pii");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Category_rejects_empty_name(string? name)
    {
        var act = () => new ClassificationCategory(name!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Classified_attribute_rejects_empty_category(string? category)
    {
        var act = () => new ClassifiedAttribute(category!);
        act.Should().Throw<ArgumentException>();
    }

    // ── ClassifiedPropertyBag scan ────────────────────────────────────────────────────────────

    [Fact]
    public void Bag_discovers_every_classified_property_and_maps_sugar_to_category()
    {
        var bag = new ClassifiedPropertyBag(typeof(Patient));

        bag.HasClassifiedFields.Should().BeTrue();
        bag.Descriptors.Select(d => d.Property.Name)
            .Should().BeEquivalentTo(new[] { "Name", "Diagnosis", "Email", "ApiKey", "Formula" });

        Category(bag, "Name").Should().Be(ClassificationCategory.Pii);
        Category(bag, "Diagnosis").Should().Be(ClassificationCategory.Phi);
        Category(bag, "ApiKey").Should().Be(ClassificationCategory.Secret);
        Category(bag, "Formula").Should().Be(new ClassificationCategory("trade-secret"));
    }

    [Fact]
    public void Bag_honours_the_Searchable_optin()
    {
        var bag = new ClassifiedPropertyBag(typeof(Patient));
        Descriptor(bag, "Email").Searchable.Should().BeTrue();
        Descriptor(bag, "Name").Searchable.Should().BeFalse();   // default
    }

    [Fact]
    public void Bag_skips_unclassified_and_readonly_properties()
    {
        var bag = new ClassifiedPropertyBag(typeof(Patient));
        bag.Descriptors.Select(d => d.Property.Name).Should().NotContain("Plain");      // unclassified
        bag.Descriptors.Select(d => d.Property.Name).Should().NotContain("Computed");   // [Pii] but get-only
    }

    [Fact]
    public void Bag_is_empty_for_an_unclassified_entity()
    {
        var bag = new ClassifiedPropertyBag(typeof(Unclassified));
        bag.HasClassifiedFields.Should().BeFalse();
        bag.Descriptors.Should().BeEmpty();
    }

    [Fact]
    public void Compiled_accessors_round_trip_the_property_value()
    {
        var bag = new ClassifiedPropertyBag(typeof(Patient));
        var name = Descriptor(bag, "Name");

        var patient = new Patient { Name = "Ada" };
        name.Getter(patient).Should().Be("Ada");

        name.Setter(patient, "ENC(Ada)");
        patient.Name.Should().Be("ENC(Ada)");          // setter wrote through to the POCO
        name.Getter(patient).Should().Be("ENC(Ada)");
    }

    // ── ClassifiedFieldRegistry off-gate + memo ───────────────────────────────────────────────

    [Fact]
    public void Registry_is_off_until_activated()
    {
        ClassifiedFieldRegistry.IsEmpty.Should().BeTrue();
        ClassifiedFieldRegistry.Activate();
        ClassifiedFieldRegistry.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Registry_returns_an_empty_bag_without_scanning_while_off()
    {
        // Off-gate: even a classified entity yields no descriptors until handling is activated (byte-identical off path).
        var off = ClassifiedFieldRegistry.ForType(typeof(Patient));
        off.HasClassifiedFields.Should().BeFalse();
        off.Descriptors.Should().BeEmpty();

        ClassifiedFieldRegistry.Activate();
        var on = ClassifiedFieldRegistry.ForType(typeof(Patient));
        on.HasClassifiedFields.Should().BeTrue();
        on.Descriptors.Should().HaveCount(5);
    }

    [Fact]
    public void Registry_memoizes_the_bag_per_type()
    {
        ClassifiedFieldRegistry.Activate();
        var first = ClassifiedFieldRegistry.ForType(typeof(Patient));
        var second = ClassifiedFieldRegistry.ForType(typeof(Patient));
        second.Should().BeSameAs(first);   // Type-plane memo: scanned + compiled once
    }

    [Fact]
    public void Reset_rearms_the_off_gate_and_clears_the_memo()
    {
        ClassifiedFieldRegistry.Activate();
        var before = ClassifiedFieldRegistry.ForType(typeof(Patient));

        ClassifiedFieldRegistry.Reset();
        ClassifiedFieldRegistry.IsEmpty.Should().BeTrue();

        ClassifiedFieldRegistry.Activate();
        var after = ClassifiedFieldRegistry.ForType(typeof(Patient));
        after.Should().NotBeSameAs(before);   // memo was cleared
    }

    private static ClassifiedFieldDescriptor Descriptor(ClassifiedPropertyBag bag, string property)
        => bag.Descriptors.Single(d => d.Property.Name == property);

    private static ClassificationCategory Category(ClassifiedPropertyBag bag, string property)
        => Descriptor(bag, property).Category;
}
