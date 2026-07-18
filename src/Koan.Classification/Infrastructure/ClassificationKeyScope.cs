using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Koan.Core.Semantics.Segmentation;

namespace Koan.Classification.Infrastructure;

/// <summary>Encodes one operation's compiled hard-segmentation bindings as a stable opaque key-provider scope.</summary>
internal static class ClassificationKeyScope
{
    public static string From(IReadOnlyList<SegmentationBinding> bindings)
    {
        if (bindings.Count == 0) return Constants.Keys.HostScope;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = new byte[4];
        foreach (var binding in bindings)
        {
            Append(binding.DimensionId);
            Append(binding.Value);
        }

        return Constants.Keys.SegmentedScopePrefix + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

        void Append(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
    }
}
