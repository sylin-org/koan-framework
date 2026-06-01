using Koan.Media.Core.Tests.Support;

namespace Koan.Media.Core.Tests.Specs.Caching;

/// <summary>
/// MEDIA-0007 §a: derivation keys are
/// <c>{sourceMediaId}:{recipeFingerprint}</c>. The shape is the foundation of
/// the cache-as-storage contract — same (source, recipe) must address the
/// same row across requests, and different (source, recipe) tuples must
/// never collide.
/// </summary>
public sealed class CacheKeySpec
{
    [Fact]
    public void SameSource_and_sameRecipe_produce_the_same_derived_key()
    {
        var recipeA = MediaRecipe.New().Resize(width: 200).EncodeAs("webp").Build();
        var recipeB = MediaRecipe.New().Resize(width: 200).EncodeAs("webp").Build();

        var keyA = StorageBackedMediaSource.DerivedKey("photo", recipeA.Fingerprint());
        var keyB = StorageBackedMediaSource.DerivedKey("photo", recipeB.Fingerprint());

        keyA.Should().Be(keyB,
            "equivalent recipes hash to the same fingerprint, so the storage row is shared");
    }

    [Fact]
    public void SameSource_with_differentRecipe_produces_different_derived_keys()
    {
        var recipe200 = MediaRecipe.New().Resize(width: 200).Build();
        var recipe400 = MediaRecipe.New().Resize(width: 400).Build();

        var key200 = StorageBackedMediaSource.DerivedKey("photo", recipe200.Fingerprint());
        var key400 = StorageBackedMediaSource.DerivedKey("photo", recipe400.Fingerprint());

        key200.Should().NotBe(key400,
            "fingerprints differ when recipe semantics differ, so derivations are addressed separately");
    }

    [Fact]
    public void DifferentSource_with_sameRecipe_produces_different_derived_keys()
    {
        var recipe = MediaRecipe.New().Resize(width: 200).EncodeAs("png").Build();
        var fingerprint = recipe.Fingerprint();

        var keyA = StorageBackedMediaSource.DerivedKey("photo-a", fingerprint);
        var keyB = StorageBackedMediaSource.DerivedKey("photo-b", fingerprint);

        keyA.Should().NotBe(keyB,
            "the sourceId prefix keeps cross-source renders from sharing a storage row");
    }

    [Fact]
    public void DifferentEncoder_format_yields_different_fingerprint_and_key()
    {
        var pngRecipe = MediaRecipe.New().Resize(width: 200).EncodeAs("png").Build();
        var webpRecipe = MediaRecipe.New().Resize(width: 200).EncodeAs("webp").Build();

        var pngKey = StorageBackedMediaSource.DerivedKey("photo", pngRecipe.Fingerprint());
        var webpKey = StorageBackedMediaSource.DerivedKey("photo", webpRecipe.Fingerprint());

        pngKey.Should().NotBe(webpKey,
            "encoder format is part of the recipe semantics — fingerprints must diverge");
    }

    [Fact]
    public void DifferentRecipeVersion_yields_different_fingerprint_and_key()
    {
        var v1 = new MediaRecipe { Version = 1 }
            with
        { Steps = MediaRecipe.New().Resize(width: 200).Build().Steps };
        var v2 = new MediaRecipe { Version = 2 }
            with
        { Steps = MediaRecipe.New().Resize(width: 200).Build().Steps };

        var k1 = StorageBackedMediaSource.DerivedKey("photo", v1.Fingerprint());
        var k2 = StorageBackedMediaSource.DerivedKey("photo", v2.Fingerprint());

        k1.Should().NotBe(k2,
            "bumping recipe Version forces a new fingerprint so old derivations don't satisfy the new recipe");
    }

    /// <summary>
    /// Sweep a small product space of distinct recipes and assert no two
    /// produce the same derived key for the same source. Acts as a guard
    /// against accidental collisions across the step grammar.
    /// </summary>
    [Fact]
    public void Distinct_recipes_never_collide_on_the_same_source()
    {
        var recipes = new List<MediaRecipe>
        {
            MediaRecipe.New().Resize(width: 200).Build(),
            MediaRecipe.New().Resize(width: 400).Build(),
            MediaRecipe.New().Resize(width: 200).EncodeAs("png").Build(),
            MediaRecipe.New().Resize(width: 200).EncodeAs("webp").Build(),
            MediaRecipe.New().Resize(width: 400).EncodeAs("png").Build(),
            MediaRecipe.New().Resize(width: 400).EncodeAs("webp").Build(),
            MediaRecipe.New().Resize(height: 300).Build(),
            MediaRecipe.New().Resize(width: 200, height: 300).Build(),
        };

        var fingerprints = recipes.Select(r => r.Fingerprint()).ToList();
        fingerprints.Should().OnlyHaveUniqueItems(
            "structurally distinct recipes must hash to distinct fingerprints");

        var keys = fingerprints.Select(f => StorageBackedMediaSource.DerivedKey("photo", f)).ToList();
        keys.Should().OnlyHaveUniqueItems(
            "no two distinct recipes may share a storage row on the same source");
    }
}
