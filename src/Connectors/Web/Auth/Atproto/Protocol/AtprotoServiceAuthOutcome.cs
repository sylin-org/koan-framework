namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>What verifying a service-auth token found.</summary>
public enum AtprotoServiceAuthOutcome
{
    /// <summary>The token is genuine, addressed here and live.</summary>
    Verified,

    /// <summary>It is not a service-auth JWT: its shape, header or claims are not the protocol's.</summary>
    Malformed,

    /// <summary>Its header names an algorithm other than ES256 and ES256K.</summary>
    UnsupportedAlgorithm,

    /// <summary>It is addressed to another service.</summary>
    WrongAudience,

    /// <summary>It is bound to another method (<c>lxm</c>).</summary>
    WrongMethod,

    /// <summary>It has expired, is not valid yet, or claims a longer life than a service-auth token may have.</summary>
    Expired,

    /// <summary>Its signature is not the issuer's: the issuer's document holds no usable key, or that key did not sign it.</summary>
    BadSignature,

    /// <summary>The issuer's DID resolves to nothing, or to a document for another DID.</summary>
    IssuerUnknown,

    /// <summary>The issuer's DID could not be resolved just now. Nothing is said about the token; a new one may succeed.</summary>
    IssuerUnreachable,
}
