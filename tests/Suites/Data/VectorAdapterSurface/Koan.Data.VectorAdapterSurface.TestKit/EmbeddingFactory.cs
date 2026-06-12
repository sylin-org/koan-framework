namespace Koan.Data.VectorAdapterSurface.TestKit;

/// <summary>
/// Deterministic test vectors so cross-adapter assertions about similarity ranking are stable.
/// The matrix asserts ranking shape (A more similar to B than to C), not absolute scores —
/// adapters normalize distance differently (cosine, L2, inner product).
/// </summary>
public static class EmbeddingFactory
{
    /// <summary>
    /// Builds a deterministic vector that points along a category-specific axis. Vectors in the
    /// same category cluster together; vectors in different categories are orthogonal-ish.
    /// </summary>
    /// <remarks>
    /// For dim ≥ 4 and N distinct categories &lt; dim, this produces a vector with most of its
    /// magnitude on one axis (the category axis) and small per-id jitter on others, so:
    /// <list type="bullet">
    ///   <item>cosine(category=A, category=A) ≈ 1</item>
    ///   <item>cosine(category=A, category=B) ≈ 0</item>
    /// </list>
    /// The jitter prevents identical vectors within a category, which lets us assert that the
    /// closest match for a query is the seed item (not a tie).
    /// </remarks>
    public static float[] ForCategory(string category, int seed, int dimension)
    {
        if (dimension < 4) throw new ArgumentOutOfRangeException(nameof(dimension), "Embedding dimension must be >= 4 for category tests.");

        // Build a category-orthogonal vector via a deterministic PRNG seeded by the category name.
        // Two distinct category strings hash to entirely different seed streams, so even on small
        // dimensions (8) the resulting unit vectors have near-zero cosine — far more reliable than
        // single-axis-per-category which collides under modulo.
        var v = new float[dimension];
        var rng = new Random(StableHash(category));
        for (int i = 0; i < dimension; i++)
        {
            v[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        }
        Normalize(v);

        // Tiny per-id jitter blended on a deterministic axis. Magnitude small enough that the
        // category-direction component dominates, so intra-category items rank ahead of
        // inter-category items in cosine search.
        var jitterAxis = ((seed % dimension) + dimension) % dimension;
        v[jitterAxis] += (seed % 17) * 0.003f;
        Normalize(v);
        return v;
    }

    /// <summary>
    /// Pseudo-random vector with a fixed seed. Useful for tests that need non-trivial vectors
    /// without semantic meaning (e.g., "delete works correctly regardless of contents").
    /// </summary>
    public static float[] Random(int seed, int dimension)
    {
        var rng = new Random(seed);
        var v = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            // Range [-1, 1]
            v[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        }
        Normalize(v);
        return v;
    }

    /// <summary>Cosine similarity in [-1, 1]; for normalized vectors equivalent to dot product.</summary>
    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        var denom = Math.Sqrt(na) * Math.Sqrt(nb);
        return denom == 0 ? 0 : dot / denom;
    }

    private static int StableHash(string category)
    {
        // Deterministic 32-bit FNV-1a hash so test embeddings are byte-stable across runs and
        // platforms (string.GetHashCode is randomized per-process in .NET).
        unchecked
        {
            const int fnvOffset = unchecked((int)2166136261u);
            const int fnvPrime = 16777619;
            int h = fnvOffset;
            foreach (var ch in category)
                h = (h ^ ch) * fnvPrime;
            return h;
        }
    }

    private static void Normalize(float[] v)
    {
        double mag = 0;
        for (int i = 0; i < v.Length; i++) mag += v[i] * v[i];
        mag = Math.Sqrt(mag);
        if (mag == 0) return;
        for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / mag);
    }
}
