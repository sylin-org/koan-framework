using AwesomeAssertions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Pipeline;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

public sealed class ClassifiedFieldFactsSpec
{
    private sealed class Patient
    {
        [Pii] public string Name { get; set; } = "";
        [Phi] public string Diagnosis { get; set; } = "";
        [Pci] public string Card { get; set; } = "";
        [Secret] public string ApiKey { get; set; } = "";
        [Classified("Trade-Secret")] public string Formula { get; set; } = "";
        public string Plain { get; set; } = "";
        [Pii] public string Computed => "read-only";
    }

    private sealed class Unclassified
    {
        public string Name { get; set; } = "";
    }

    [Fact]
    public void Well_known_categories_are_stable()
    {
        ClassificationCategory.Pii.Name.Should().Be("pii");
        ClassificationCategory.Phi.Name.Should().Be("phi");
        ClassificationCategory.Pci.Name.Should().Be("pci");
        ClassificationCategory.Secret.Name.Should().Be("secret");
    }

    [Fact]
    public void Custom_categories_are_trimmed_and_normalized()
    {
        new ClassificationCategory(" Trade-Secret ").Should().Be(new ClassificationCategory("trade-secret"));
        new ClassifiedAttribute(" TRADE-SECRET ").Category.Name.Should().Be("trade-secret");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_categories_are_rejected(string? category)
    {
        var value = () => new ClassificationCategory(category!);
        value.Should().Throw<ArgumentException>();
        var attribute = () => new ClassifiedAttribute(category!);
        attribute.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Bag_discovers_writable_classified_properties_and_categories()
    {
        var bag = new ClassifiedPropertyBag(typeof(Patient));
        bag.HasClassifiedFields.Should().BeTrue();
        bag.Descriptors.Select(descriptor => descriptor.Property.Name).Should().BeEquivalentTo(
            "Name", "Diagnosis", "Card", "ApiKey", "Formula");
        Category(bag, "Name").Should().Be(ClassificationCategory.Pii);
        Category(bag, "Diagnosis").Should().Be(ClassificationCategory.Phi);
        Category(bag, "Card").Should().Be(ClassificationCategory.Pci);
        Category(bag, "ApiKey").Should().Be(ClassificationCategory.Secret);
        Category(bag, "Formula").Name.Should().Be("trade-secret");
    }

    [Fact]
    public void Bag_skips_unclassified_and_readonly_properties()
    {
        var names = new ClassifiedPropertyBag(typeof(Patient)).Descriptors.Select(descriptor => descriptor.Property.Name);
        names.Should().NotContain("Plain").And.NotContain("Computed");
    }

    [Fact]
    public void Bag_is_empty_for_an_unclassified_type()
    {
        var bag = new ClassifiedPropertyBag(typeof(Unclassified));
        bag.HasClassifiedFields.Should().BeFalse();
        bag.Descriptors.Should().BeEmpty();
    }

    [Fact]
    public void Compiled_accessors_round_trip_the_property_value()
    {
        var descriptor = Descriptor(new ClassifiedPropertyBag(typeof(Patient)), "Name");
        var patient = new Patient { Name = "Ada" };
        descriptor.Getter(patient).Should().Be("Ada");
        descriptor.Setter(patient, "protected");
        patient.Name.Should().Be("protected");
    }

    private static ClassifiedFieldDescriptor Descriptor(ClassifiedPropertyBag bag, string property)
        => bag.Descriptors.Single(descriptor => descriptor.Property.Name == property);

    private static ClassificationCategory Category(ClassifiedPropertyBag bag, string property)
        => Descriptor(bag, property).Category;
}
