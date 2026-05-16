using Koan.Rag.Abstractions;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Distillation;

/// <summary>
/// Diagonal-covariance Gaussian Mixture Model clustering with UMAP
/// dimensionality reduction and BIC-based k selection. Supports soft
/// clustering: embeddings near cluster boundaries are assigned to
/// multiple clusters (probability threshold 0.1).
/// <para>
/// This is the default <see cref="IClusteringStrategy"/> for RAPTOR tree
/// construction. Pure C# implementation with the UMAP NuGet package
/// as the only external dependency.
/// </para>
/// </summary>
internal sealed class DiagonalGmmClustering : IClusteringStrategy
{
    private readonly ILogger<DiagonalGmmClustering> _logger;
    private const int UmapTargetDimensions = 10;
    private const int MaxBicCandidates = 50;
    private const int MaxEmIterations = 30;
    private const double ConvergenceThreshold = 1e-4;
    private const double SoftAssignmentThreshold = 0.1;
    private const int BicEarlyStopPatience = 3;

    public DiagonalGmmClustering(ILogger<DiagonalGmmClustering> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<ClusterAssignment>> Cluster(
        IReadOnlyList<EmbeddingWithId> embeddings,
        int targetClusters,
        CancellationToken ct = default)
    {
        if (embeddings.Count < 2)
        {
            // Trivial case: one or zero items → single cluster
            return [new ClusterAssignment
            {
                ClusterId = 0,
                MemberIds = embeddings.Select(e => e.Id).ToList(),
                Centroid = embeddings.Count > 0 ? embeddings[0].Embedding : []
            }];
        }

        // Step 1: Reduce dimensionality via UMAP
        var reduced = await ReduceDimensionality(embeddings, ct);

        // Step 2: Select optimal k via BIC
        var maxK = Math.Min(targetClusters, Math.Min(MaxBicCandidates, embeddings.Count / 2));
        var (optimalK, gmm) = SelectKViaBic(reduced, maxK);

        _logger.LogDebug(
            "GMM selected k={K} clusters for {N} embeddings (target was {Target})",
            optimalK, embeddings.Count, targetClusters);

        // Step 4: Soft assignment
        var assignments = SoftAssign(embeddings, reduced, gmm);

        return assignments;
    }

    // ── UMAP Dimensionality Reduction ───────────────────────────────────

    private Task<float[][]> ReduceDimensionality(
        IReadOnlyList<EmbeddingWithId> embeddings,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var inputDim = embeddings[0].Embedding.Length;

            if (inputDim <= UmapTargetDimensions)
            {
                // Already low-dimensional, skip UMAP
                return embeddings.Select(e => e.Embedding).ToArray();
            }

            var nNeighbors = Math.Max(2, (int)Math.Sqrt(embeddings.Count - 1));
            nNeighbors = Math.Min(nNeighbors, embeddings.Count - 1);

            var umap = new UMAP.Umap(
                distance: UMAP.Umap.DistanceFunctions.Cosine,
                dimensions: UmapTargetDimensions,
                numberOfNeighbors: nNeighbors);

            var data = embeddings.Select(e => e.Embedding).ToArray();
            var epochs = umap.InitializeFit(data);

            for (var i = 0; i < epochs; i++)
            {
                ct.ThrowIfCancellationRequested();
                umap.Step();
            }

            return umap.GetEmbedding();
        }, ct);
    }

    // ── BIC-Based K Selection ───────────────────────────────────────────

    private (int k, GmmModel model) SelectKViaBic(float[][] data, int maxK)
    {
        var bestK = 1;
        var bestBic = double.MaxValue;
        GmmModel? bestGmm = null;
        var consecutiveIncreases = 0;

        for (var k = 1; k <= maxK; k++)
        {
            var gmm = FitGmm(data, k);
            var logLikelihood = ComputeLogLikelihood(data, gmm);
            var numParams = k * (2 * data[0].Length + 1) - 1; // diagonal covariance
            var bic = -2 * logLikelihood + numParams * Math.Log(data.Length);

            if (bic < bestBic)
            {
                bestBic = bic;
                bestK = k;
                bestGmm = gmm;
                consecutiveIncreases = 0;
            }
            else
            {
                consecutiveIncreases++;
                if (consecutiveIncreases >= BicEarlyStopPatience)
                    break;
            }
        }

        return (bestK, bestGmm ?? throw new InvalidOperationException(
            $"BIC selection produced no model (maxK={maxK}). This should not happen."));
    }

    // ── Diagonal GMM with EM ────────────────────────────────────────────

    private GmmModel FitGmm(float[][] data, int k, int seed = 42)
    {
        var n = data.Length;
        var d = data[0].Length;
        var rng = new Random(seed + k); // Deterministic but k-dependent for reproducibility

        // Initialize: random means from data, unit variance, equal weights
        var means = new double[k][];
        var variances = new double[k][];
        var weights = new double[k];

        var indices = Enumerable.Range(0, n).OrderBy(_ => rng.Next()).Take(k).ToArray();
        for (var c = 0; c < k; c++)
        {
            means[c] = data[indices[c]].Select(v => (double)v).ToArray();
            variances[c] = Enumerable.Repeat(1.0, d).ToArray();
            weights[c] = 1.0 / k;
        }

        // EM iterations
        var responsibilities = new double[n, k];
        var prevLogLikelihood = double.NegativeInfinity;

        for (var iter = 0; iter < MaxEmIterations; iter++)
        {
            // E-step: compute responsibilities via log-sum-exp to prevent underflow
            var logProbs = new double[k];
            for (var i = 0; i < n; i++)
            {
                var maxLogProb = double.NegativeInfinity;
                for (var c = 0; c < k; c++)
                {
                    logProbs[c] = Math.Log(weights[c] + 1e-300) +
                                  DiagonalGaussianLogPdf(data[i], means[c], variances[c]);
                    if (logProbs[c] > maxLogProb) maxLogProb = logProbs[c];
                }

                var sumExp = 0.0;
                for (var c = 0; c < k; c++)
                    sumExp += Math.Exp(logProbs[c] - maxLogProb);
                var logNorm = maxLogProb + Math.Log(sumExp + 1e-300);

                for (var c = 0; c < k; c++)
                    responsibilities[i, c] = Math.Exp(logProbs[c] - logNorm);
            }

            // M-step: update parameters
            for (var c = 0; c < k; c++)
            {
                var nk = 0.0;
                for (var i = 0; i < n; i++) nk += responsibilities[i, c];

                if (nk < 1e-10)
                {
                    weights[c] = 1e-10;
                    continue;
                }

                weights[c] = nk / n;

                // Update mean
                for (var j = 0; j < d; j++)
                {
                    var sum = 0.0;
                    for (var i = 0; i < n; i++)
                        sum += responsibilities[i, c] * data[i][j];
                    means[c][j] = sum / nk;
                }

                // Update variance (diagonal)
                for (var j = 0; j < d; j++)
                {
                    var sum = 0.0;
                    for (var i = 0; i < n; i++)
                    {
                        var diff = data[i][j] - means[c][j];
                        sum += responsibilities[i, c] * diff * diff;
                    }

                    variances[c][j] = Math.Max(sum / nk, 1e-6); // Floor to prevent singularity
                }
            }

            // Check convergence
            var logLikelihood = ComputeLogLikelihood(data,
                new GmmModel(means, variances, weights));

            if (Math.Abs(logLikelihood - prevLogLikelihood) < ConvergenceThreshold)
                break;

            prevLogLikelihood = logLikelihood;
        }

        return new GmmModel(means, variances, weights);
    }

    // ── Soft Assignment ─────────────────────────────────────────────────

    private static IReadOnlyList<ClusterAssignment> SoftAssign(
        IReadOnlyList<EmbeddingWithId> originalEmbeddings,
        float[][] reducedData,
        GmmModel gmm)
    {
        var k = gmm.Weights.Length;
        var n = reducedData.Length;
        var clusterMembers = new List<string>[k];

        for (var c = 0; c < k; c++)
            clusterMembers[c] = [];

        // Compute responsibilities via log-sum-exp and assign to all clusters above threshold
        var logProbs = new double[k];
        for (var i = 0; i < n; i++)
        {
            var maxLogProb = double.NegativeInfinity;
            for (var c = 0; c < k; c++)
            {
                logProbs[c] = Math.Log(gmm.Weights[c] + 1e-300) +
                              DiagonalGaussianLogPdf(reducedData[i], gmm.Means[c], gmm.Variances[c]);
                if (logProbs[c] > maxLogProb) maxLogProb = logProbs[c];
            }

            var sumExp = 0.0;
            for (var c = 0; c < k; c++)
                sumExp += Math.Exp(logProbs[c] - maxLogProb);
            var logNorm = maxLogProb + Math.Log(sumExp + 1e-300);

            for (var c = 0; c < k; c++)
            {
                var prob = Math.Exp(logProbs[c] - logNorm);
                if (prob >= SoftAssignmentThreshold)
                    clusterMembers[c].Add(originalEmbeddings[i].Id);
            }
        }

        // Build cluster assignments (skip empty clusters)
        var assignments = new List<ClusterAssignment>();
        for (var c = 0; c < k; c++)
        {
            if (clusterMembers[c].Count == 0) continue;

            assignments.Add(new ClusterAssignment
            {
                ClusterId = c,
                MemberIds = clusterMembers[c],
                Centroid = gmm.Means[c].Select(v => (float)v).ToArray()
            });
        }

        return assignments;
    }

    // ── Math Utilities ──────────────────────────────────────────────────

    private static double DiagonalGaussianLogPdf(float[] x, double[] mean, double[] variance)
    {
        var d = x.Length;
        var logProb = -0.5 * d * Math.Log(2 * Math.PI);

        for (var j = 0; j < d; j++)
        {
            var diff = x[j] - mean[j];
            logProb -= 0.5 * Math.Log(variance[j] + 1e-300);
            logProb -= 0.5 * diff * diff / (variance[j] + 1e-300);
        }

        return logProb;
    }

    private static double ComputeLogLikelihood(float[][] data, GmmModel gmm)
    {
        var logLikelihood = 0.0;
        var k = gmm.Weights.Length;
        var componentLogProbs = new double[k];

        foreach (var point in data)
        {
            // Log-sum-exp over components to prevent underflow
            var maxLogProb = double.NegativeInfinity;
            for (var c = 0; c < k; c++)
            {
                componentLogProbs[c] = Math.Log(gmm.Weights[c] + 1e-300) +
                                       DiagonalGaussianLogPdf(point, gmm.Means[c], gmm.Variances[c]);
                if (componentLogProbs[c] > maxLogProb) maxLogProb = componentLogProbs[c];
            }

            var sumExp = 0.0;
            for (var c = 0; c < k; c++)
                sumExp += Math.Exp(componentLogProbs[c] - maxLogProb);

            logLikelihood += maxLogProb + Math.Log(sumExp + 1e-300);
        }

        return logLikelihood;
    }

    /// <summary>Internal GMM model parameters.</summary>
    private sealed record GmmModel(double[][] Means, double[][] Variances, double[] Weights);
}
