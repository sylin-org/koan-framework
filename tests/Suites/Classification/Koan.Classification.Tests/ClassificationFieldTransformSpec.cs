using AwesomeAssertions;
using Koan.Classification.Crypto;
using Koan.Classification.Pipeline;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Pipeline;
using Xunit;

namespace Koan.Classification.Tests;

public sealed class ClassificationFieldTransformSpec
{
    private sealed class Patient
    {
        [Pii] public string Name { get; set; } = "";
        [Phi] public string? Diagnosis { get; set; }
        public string Plain { get; set; } = "";
    }

    private sealed class BadEntity
    {
        [Pii] public int Ssn { get; set; }
    }

    private static ClassificationFieldTransform Transform(IClassificationKeyProvider? keys = null)
        => new(
            new AesGcmFieldCipher(),
            keys ?? new EphemeralClassificationKeyProvider(),
            SegmentationPlan.Empty.Untyped,
            new ClassifiedPropertyBag(typeof(Patient)));

    [Fact]
    public void Encrypts_classified_fields_and_leaves_plain_ones()
    {
        var transform = Transform();
        var patient = new Patient { Name = "Ada", Diagnosis = "influenza", Plain = "ward-3" };
        transform.ApplyOnWrite(patient);
        FieldCipherEnvelope.TryParse(patient.Name, out _).Should().BeTrue();
        FieldCipherEnvelope.TryParse(patient.Diagnosis, out _).Should().BeTrue();
        patient.Plain.Should().Be("ward-3");
    }

    [Fact]
    public void Write_then_read_round_trips()
    {
        var transform = Transform();
        var patient = new Patient { Name = "Ada", Diagnosis = "influenza" };
        transform.ApplyOnWrite(patient);
        transform.ApplyOnRead(patient);
        patient.Name.Should().Be("Ada");
        patient.Diagnosis.Should().Be("influenza");
    }

    [Fact]
    public void Null_and_legacy_plaintext_remain_unchanged()
    {
        var transform = Transform();
        var patient = new Patient { Name = "legacy", Diagnosis = null };
        transform.ApplyOnRead(patient);
        patient.Name.Should().Be("legacy");
        patient.Diagnosis.Should().BeNull();
    }

    [Fact]
    public void Protected_write_is_idempotent()
    {
        var transform = Transform();
        var patient = new Patient { Name = "Ada" };
        transform.ApplyOnWrite(patient);
        var stored = patient.Name;
        transform.ApplyOnWrite(patient);
        patient.Name.Should().Be(stored);
    }

    [Fact]
    public void Empty_string_round_trips_distinctly_from_null()
    {
        var transform = Transform();
        var patient = new Patient { Name = "", Diagnosis = null };
        transform.ApplyOnWrite(patient);
        FieldCipherEnvelope.TryParse(patient.Name, out _).Should().BeTrue();
        transform.ApplyOnRead(patient);
        patient.Name.Should().Be("");
        patient.Diagnosis.Should().BeNull();
    }

    [Fact]
    public void Malformed_reserved_envelope_fails_loudly()
    {
        var transform = Transform();
        var patient = new Patient { Name = "kfe1:AAAA" };
        var read = () => transform.ApplyOnRead(patient);
        read.Should().Throw<ClassificationIntegrityException>();
    }

    [Fact]
    public void Missing_keys_fail_loudly_instead_of_returning_a_tombstone()
    {
        var keys = new EphemeralClassificationKeyProvider();
        var transform = Transform(keys);
        var patient = new Patient { Name = "Ada" };
        transform.ApplyOnWrite(patient);
        keys.Dispose();
        var read = () => transform.ApplyOnRead(patient);
        read.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Non_string_classified_property_is_rejected_during_plan_construction()
    {
        var act = () => new ClassificationFieldTransform(
            new AesGcmFieldCipher(),
            new EphemeralClassificationKeyProvider(),
            SegmentationPlan.Empty.Untyped,
            new ClassifiedPropertyBag(typeof(BadEntity)));
        act.Should().Throw<NotSupportedException>().WithMessage("*string properties only*");
    }
}
