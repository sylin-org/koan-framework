using System.Reflection;
using System.Security.Claims;
using AwesomeAssertions;
using Koan.Data.Core.Model;
using Koan.Web.Authorization;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// SEC-0012: an entity's <c>[Authorize]</c> lowers into the gate only when the gate can express it. A named policy, or
/// authentication schemes, used to lower to "any authenticated principal" on REST and MCP; it is now refused.
/// </summary>
public sealed class AccessGatePolicyLoweringSpec
{
    [Fact]
    public void A_named_policy_is_refused_rather_than_lowered_to_authenticated()
    {
        Action compile = () => AccessGateCache.Compile(typeof(PolicyOnly));

        compile.Should().Throw<AccessGateException>()
            .WithMessage("*[Authorize(Policy = \"AdminOnly\")] on PolicyOnly cannot be enforced*[Access(*");
    }

    [Fact]
    public void Authentication_schemes_are_refused()
    {
        Action compile = () => AccessGateCache.Compile(typeof(SchemesOnly));

        compile.Should().Throw<AccessGateException>().WithMessage("*AuthenticationSchemes = \"Bearer\"*");
    }

    [Fact]
    public void The_scope_policy_convention_lowers_to_a_scope_grant()
    {
        var gate = AccessGateCache.Compile(typeof(ScopePolicy)).For(EntityAuthorizeActions.Write);

        AccessGateEvaluator.Evaluate(gate, User("items:write"), ownerSatisfied: false).Should().BeOfType<AuthorizeDecision.Allow>();
        AccessGateEvaluator.Evaluate(gate, User("items:read"), ownerSatisfied: false).Should().BeOfType<AuthorizeDecision.Forbid>(
            "an authenticated caller without the scope is not admitted");
    }

    [Fact]
    public void Boot_refuses_an_entity_whose_policy_the_gate_cannot_enforce()
    {
        var application = new DeclaredAssembly(new PolicyEntity(), typeof(PolicyController));
        Action validate = () => AccessGateRegistrar.Validate([application]);

        validate.Should().Throw<AccessGateException>()
            .WithMessage("1 malformed [Access] declaration(s) found at boot:*Policy = \"AdminOnly\"*ShapedEntity*");
    }

    [Fact]
    public void A_controller_keeps_its_named_policy()
    {
        var application = new DeclaredAssembly(typeof(PolicyController));
        Action validate = () => AccessGateRegistrar.Validate([application]);

        validate.Should().NotThrow("ASP.NET evaluates a controller's policy as endpoint metadata");
    }

    private static ClaimsPrincipal User(string scopes)
        => new(new ClaimsIdentity([new Claim("scope", scopes)], "Test"));

    [Authorize(Policy = "AdminOnly")]
    private sealed class PolicyOnly;

    [Authorize(AuthenticationSchemes = "Bearer")]
    private sealed class SchemesOnly;

    [Authorize(Policy = "scope:items:write")]
    private sealed class ScopePolicy;

    [Authorize(Policy = "AdminOnly")]
    private sealed class PolicyController;

    // A real attribute on a real Entity would refuse every AddKoan host that discovers this assembly, so the entity's
    // [Authorize] exists only in supplied reflection metadata (as in AccessGateDiscoverySpec).
    private sealed class ShapedEntity : Entity<ShapedEntity>;

    private sealed class PolicyEntity() : TypeDelegator(typeof(ShapedEntity))
    {
        private readonly AuthorizeAttribute _authorize = new() { Policy = "AdminOnly" };
        public override object[] GetCustomAttributes(bool inherit) => new Attribute[] { _authorize };
        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
            => attributeType.IsAssignableFrom(typeof(AuthorizeAttribute))
                ? new AuthorizeAttribute[] { _authorize }
                : (object[])Array.CreateInstance(attributeType, 0);
        public override bool IsDefined(Type attributeType, bool inherit) => attributeType.IsAssignableFrom(typeof(AuthorizeAttribute));
        public override PropertyInfo[] GetProperties(BindingFlags bindingAttr) => [];
    }

    private sealed class DeclaredAssembly(params Type[] types) : Assembly
    {
        public override AssemblyName[] GetReferencedAssemblies()
            => [typeof(AuthorizeAttribute).Assembly.GetName(), typeof(Koan.Data.Abstractions.IEntity<>).Assembly.GetName()];
        public override Type[] GetTypes() => types;
    }
}
