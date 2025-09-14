using System.Security.Cryptography;
using System.Text;

namespace S5.Recs.Infrastructure;

/// <summary>
/// Preemptive tag filter using MD5 hashes to automatically censor problematic tags during import.
/// This approach avoids storing sensitive terms directly in code while enabling automated moderation.
///
/// To update this list:
/// 1. Ensure your censor list in the database contains the desired tags
/// 2. Call /admin/tags/censor/hashes to generate new hash list
/// 3. Replace the hashes in PreemptiveHashes with the generated values
/// 4. Redeploy the application
/// </summary>
internal static class PreemptiveTagFilter
{
    /// <summary>
    /// MD5 hashes of normalized (lowercase, trimmed) tags that should be automatically censored.
    /// Generated from existing censor list - update using /admin/tags/censor/hashes endpoint.
    ///
    /// NOTE: Update this list periodically based on your censor database:
    /// 1. GET /admin/tags/censor/hashes
    /// 2. Replace the array below with the "hashes" from the response
    /// 3. Redeploy
    /// </summary>
    private static readonly HashSet<string> PreemptiveHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        "0137d475906f87adef786445fdf4992d",
        "026b31f7f440503a3bf40653d5857086",
        "02b7b2b6c595195943b35ce38df20afd",
        "03b45bce682586cafb346f27e076d97b",
        "07a144f198086f7340648e75875de546",
        "0fa9d94de8921144b76e47749b4a168b",
        "1893596d37a5a39a699642a903e18cb3",
        "1edce1fd34d42a36402ad97089688c7b",
        "2b7c1bc93ca6f6e6bba7d21659f43bc5",
        "2dbe6b97b74ba9f4854d042b1463d6cd",
        "2e1b6c4a6aa9def651396d5b77122989",
        "3221031e8ed5a17ec9a57271de7fadfe",
        "33ecee1c6ea03468b351f95aff9cbfed",
        "347341c085ab1ff63e8409ac9073da9f",
        "39e184020c59829b0abb8449e772072f",
        "3c3662bcb661d6de679c636744c66b62",
        "47f66c469fd43fd02bce971ace163f24",
        "4d238f0545059ca02bb4f65e08dcdc74",
        "67c480d10c82f4d7eea4afaf52c985c0",
        "69b348121db34c19fb2d2d7c7e2aeaf6",
        "6b4730f4ef09d2de22242706c332ed4b",
        "7382eb2379a5eb17630d1c43dce03693",
        "8052061e13ccfa6e4bcff75cb39658eb",
        "826ba61ead98ee74fc18e31641a12099",
        "8925346234c54d3d2b87d755c574ceed",
        "8a9b9237322f5653fb34c07f2e14bd1a",
        "912897db01678cc0d2147154dddba25b",
        "95f7711cfd1dd49a988ca95454764023",
        "99e9bae675b12967251c175696f00a70",
        "a21812476f7db96864dd9c5bedd90483",
        "a88db991033ac09fc2353859c8aae90b",
        "ac5d116f7fc6aefad750520dad410fd8",
        "ad0e9d5ed077db5266ff315985114a4f",
        "c0a5c207178ceb3c84fdd82ddfff5d00",
        "c29b0f2822df70edc474f6f47d3c0dc3",
        "c34c8b2c24e2aa2e45d62f1e15967bd2",
        "c3734505c7e4916559bb44b99847b089",
        "cee8289955b8c477b605b86c2fa861da",
        "cefc78f5c9e6260d999865c17b231af7",
        "dba35fa1d52852fe259d972567bb73c1",
        "dbf6dc4dfdfa929bd73bcad2b0b6e45f",
        "de6ad1cd521db1ee7397e4ed9f18a212",
        "dfcdc324303fb7b22e73e84820690256",
        "e03f9063484fb1967d1675c86a6094d7",
        "ed842a85d536010f418177f6ea4ce2bc",
        "fcfd9f4c146b22905eaeb8796f7a00bf",
        "fd95520e3fae970d59ef92a3b7c2adce"
    };

    /// <summary>
    /// Checks if a tag should be preemptively censored based on its MD5 hash.
    /// </summary>
    /// <param name="tag">The tag to check</param>
    /// <returns>True if the tag should be automatically censored</returns>
    public static bool ShouldCensor(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;

        var normalizedTag = tag.Trim().ToLowerInvariant();
        var hash = ComputeMD5Hash(normalizedTag);
        return PreemptiveHashes.Contains(hash);
    }

    /// <summary>
    /// Computes MD5 hash for a normalized tag (lowercase, trimmed).
    /// </summary>
    /// <param name="normalizedTag">The normalized tag</param>
    /// <returns>Lowercase hexadecimal MD5 hash</returns>
    private static string ComputeMD5Hash(string normalizedTag)
    {
        var bytes = Encoding.UTF8.GetBytes(normalizedTag);
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Gets the count of preemptive hashes currently loaded.
    /// Useful for diagnostics and verification.
    /// </summary>
    public static int PreemptiveHashCount => PreemptiveHashes.Count;

    /// <summary>
    /// For debugging: get the hash of a specific tag to verify it matches your list.
    /// </summary>
    /// <param name="tag">Tag to hash</param>
    /// <returns>MD5 hash that would be checked against the preemptive list</returns>
    public static string GetHashForTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return string.Empty;
        var normalizedTag = tag.Trim().ToLowerInvariant();
        return ComputeMD5Hash(normalizedTag);
    }
}