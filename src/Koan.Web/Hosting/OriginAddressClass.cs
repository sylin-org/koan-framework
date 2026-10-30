namespace Koan.Web.Hosting;

/// <summary>
/// WEB-0081: what kind of address an observed origin's host is, so an application can offer only an origin the public
/// reaches. A DNS name is <see cref="Public"/> unless it is <c>localhost</c> or under <c>.localhost</c> (RFC 6761); Koan
/// does not resolve names to classify them.
/// </summary>
public enum OriginAddressClass
{
    /// <summary>A DNS name, or an IP address outside every range below.</summary>
    Public,

    /// <summary>A private, shared or link-local IP address: 10/8, 172.16/12, 192.168/16, 100.64/10, 169.254/16, fc00::/7, fe80::/10.</summary>
    Private,

    /// <summary><c>localhost</c>, a name under <c>.localhost</c>, 127/8 or <c>::1</c>: this machine only.</summary>
    Loopback,

    /// <summary><c>0.0.0.0</c> or <c>::</c>: where a server binds, not an address anyone reaches it at.</summary>
    Unspecified,
}
