using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Koan.Cache.Analyzers;

/// <summary>
/// KOAN0001 — Use typed cache registration helper instead of bare
/// <c>TryAddEnumerable(ServiceDescriptor.Singleton&lt;I&gt;(...))</c> for framework-managed
/// cache interfaces. See ARCH-0081.
/// </summary>
/// <remarks>
/// The bare descriptor form's <c>ImplementationType</c> equals the service interface, which
/// <c>TryAddEnumerable</c> rejects at runtime as "indistinguishable" — first registration
/// throws and the host fails to boot. The framework ships typed helpers
/// (<c>AddCacheStore&lt;T&gt;</c>, <c>AddCoherenceChannel&lt;T&gt;</c>) that bake in the
/// correct two-generic <c>Singleton&lt;TService, TImpl&gt;(factory)</c> shape. This analyzer
/// flags adapter code that bypasses them for <c>ICacheStore</c> / <c>ICacheCoherenceChannel</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CacheRegistrationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "KOAN0001";

    private static readonly LocalizableString Title =
        "Use typed cache registration helper";

    private static readonly LocalizableString MessageFormat =
        "Use the typed helper for '{0}' instead of bare TryAddEnumerable(ServiceDescriptor.Singleton<{0}>(...)). " +
        "Call '{1}<TImpl>()' from Koan.Cache.Abstractions.Extensions.CacheRegistrationExtensions.";

    private static readonly LocalizableString Description =
        "Bare TryAddEnumerable with a single-generic ServiceDescriptor.Singleton factory produces an indistinguishable descriptor " +
        "(ImplementationType equals ServiceType) and throws at runtime when a second registration is attempted. The typed helpers " +
        "ship the correct two-generic descriptor shape. See ARCH-0081 for the rationale.";

    private const string Category = "Koan.Cache.Registration";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: Title,
        messageFormat: MessageFormat,
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/sylin-org/koan-framework/blob/main/docs/decisions/ARCH-0081-typed-registration-helpers-and-analyzer.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <summary>
    /// Map from fully-qualified service interface → the typed helper that should be used.
    /// Adding a new pillar interface to the canon: append a new pair here.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownInterfaces =
        new Dictionary<string, string>
        {
            ["Koan.Cache.Abstractions.Stores.ICacheStore"] = "AddCacheStore",
            ["Koan.Cache.Abstractions.Coherence.ICacheCoherenceChannel"] = "AddCoherenceChannel",
        };

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation) return;

        // Must look like `something.TryAddEnumerable(...)`.
        if (invocation.Expression is not MemberAccessExpressionSyntax tryAddAccess) return;
        if (tryAddAccess.Name.Identifier.ValueText != "TryAddEnumerable") return;

        // Resolve the symbol; it must be ServiceCollectionDescriptorExtensions.TryAddEnumerable.
        var tryAddSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (tryAddSymbol is null) return;
        if (tryAddSymbol.Name != "TryAddEnumerable") return;
        if (tryAddSymbol.ContainingType?.ToDisplayString() != "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions")
            return;

        // The first argument must be a `ServiceDescriptor.Singleton<T>(...)` call with one generic.
        if (invocation.ArgumentList.Arguments.Count == 0) return;
        if (invocation.ArgumentList.Arguments[0].Expression is not InvocationExpressionSyntax descriptorCall) return;
        if (descriptorCall.Expression is not MemberAccessExpressionSyntax descriptorAccess) return;

        // Must be Singleton<T> with exactly one type argument (the bad shape).
        if (descriptorAccess.Name is not GenericNameSyntax singletonGeneric) return;
        if (singletonGeneric.Identifier.ValueText != "Singleton") return;
        if (singletonGeneric.TypeArgumentList.Arguments.Count != 1) return;

        // Resolve to confirm it's ServiceDescriptor.Singleton<TService>(factory) — the
        // single-generic factory overload specifically.
        var singletonSymbol = context.SemanticModel.GetSymbolInfo(descriptorCall).Symbol as IMethodSymbol;
        if (singletonSymbol is null) return;
        if (singletonSymbol.Name != "Singleton") return;
        if (singletonSymbol.ContainingType?.ToDisplayString() != "Microsoft.Extensions.DependencyInjection.ServiceDescriptor")
            return;
        if (singletonSymbol.TypeParameters.Length != 1) return;

        // Check the generic argument against the canon list.
        var typeArgSymbol = context.SemanticModel.GetSymbolInfo(singletonGeneric.TypeArgumentList.Arguments[0]).Symbol;
        if (typeArgSymbol is not INamedTypeSymbol namedType) return;

        var serviceTypeName = namedType.OriginalDefinition.ToDisplayString();
        if (!KnownInterfaces.TryGetValue(serviceTypeName, out var helperName)) return;

        var diagnostic = Diagnostic.Create(
            Rule,
            descriptorCall.GetLocation(),
            namedType.Name,
            helperName);

        context.ReportDiagnostic(diagnostic);
    }
}
