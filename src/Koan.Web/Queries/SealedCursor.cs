using System.Security.Cryptography;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Querying;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace Koan.Web.Queries;

/// <summary>
/// A list's <see cref="KeysetKey"/> as an address carries it (DATA-0170): sealed with ASP.NET Data Protection under one purpose per list, so
/// a reader holds where the list continues without reading the values it holds, and a key altered, or sealed for another list, never
/// opens. A declared collection seals its own; an application that pages a list itself seals its key here.
/// <code>
/// var after = SealedCursor.Open(protection, "receipts", Receipts.Newest, query["after"]);
/// if (query.ContainsKey("after") &amp;&amp; after is null) return this.Refused(SealedCursor.Refusal());
/// var page = await Receipt.Page(Receipts.Newest, after, 50, ct);
/// var next = page.Next is { } key ? SealedCursor.Seal(protection, "receipts", Receipts.Newest, key) : null;
/// </code>
/// </summary>
public static class SealedCursor
{
    /// <summary><paramref name="key"/> sealed for the list <paramref name="purpose"/> names.</summary>
    public static string Seal<TEntity>(IDataProtectionProvider protection, string purpose, Keyset<TEntity> order, KeysetKey key)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(protection);
        ArgumentNullException.ThrowIfNull(order);
        return Protector(protection, purpose).Protect(order.Write(key));
    }

    /// <summary>The key <paramref name="text"/> holds for the list <paramref name="purpose"/> names, or null where it holds none: empty,
    /// altered, sealed for another list or by another key ring, or not a key of <paramref name="order"/>.</summary>
    public static KeysetKey? Open<TEntity>(IDataProtectionProvider protection, string purpose, Keyset<TEntity> order, string? text)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(protection);
        ArgumentNullException.ThrowIfNull(order);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return order.Read(Protector(protection, purpose).Unprotect(text));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>What a request whose <paramref name="field"/> does not open is refused with: 400, naming the field.</summary>
    public static Reason Refusal(string field = KoanWebConstants.Query.After)
        => Reason.Invalid(field, $"This '{field}' does not continue this list. Leave it out to read from the start.");

    private static IDataProtector Protector(IDataProtectionProvider protection, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return protection.CreateProtector(KoanWebConstants.Keyset.Purpose, purpose);
    }
}
