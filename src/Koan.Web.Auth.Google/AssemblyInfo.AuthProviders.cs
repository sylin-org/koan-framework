using Koan.Web.Auth.Attributes;
using Koan.Web.Auth.Infrastructure;

[assembly: AuthProviderDescriptor("google", "Google", AuthConstants.Protocols.Oidc, Icon = "/icons/google.svg")]
