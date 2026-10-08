// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// Enforces scoped access to generated native owners and their allocation-free leases.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SafeHandleAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Reports lease creation, copying, storage, or disposal outside the supported using-local pattern.
    /// </summary>
    public static readonly DiagnosticDescriptor LeaseMustBeUsingLocal = new(
        "PInvoke015",
        "Keep leases in using locals",
        "Create each lease with 'using var lease = owner.Lease()'; do not copy, store, pass, return, or explicitly dispose it",
        "Lifetime",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports native value access without a visible lifetime scope.
    /// </summary>
    public static readonly DiagnosticDescriptor ValueNeedsScope = new(
        "PInvoke016",
        "Keep the native resource alive",
        "Read Value inside the owner's using scope, or acquire a lease in a using local",
        "Lifetime",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports raw resource values saved or returned instead of consumed within their scope.
    /// </summary>
    public static readonly DiagnosticDescriptor ValueMustNotEscape = new(
        "PInvoke017",
        "Do not save or return raw resources",
        "Use Value directly in a call or copy its contents; do not save or return the raw resource",
        "Lifetime",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports attempts to release a resource that still belongs to its SafeHandle.
    /// </summary>
    public static readonly DiagnosticDescriptor ValueMustNotRelease = new(
        "PInvoke018",
        "Do not release an owned resource",
        "Do not pass Value to '{0}'; dispose the SafeHandle instead",
        "Lifetime",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        LeaseMustBeUsingLocal, ValueNeedsScope, ValueMustNotEscape, ValueMustNotRelease);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeLocal, SyntaxKind.VariableDeclarator);
        context.RegisterSymbolAction(AnalyzeDeclaration, SymbolKind.Field, SymbolKind.Parameter, SymbolKind.Property, SymbolKind.Method);
        context.RegisterOperationAction(AnalyzeLeaseCreation, OperationKind.Invocation, OperationKind.ObjectCreation, OperationKind.DefaultValue);
        context.RegisterOperationAction(AnalyzeLeaseReference, OperationKind.LocalReference);
        context.RegisterOperationAction(AnalyzeValue, OperationKind.PropertyReference);
    }

    private static AttributeData? GetOwnership(ISymbol symbol) => symbol.GetAttributes().FirstOrDefault(attribute =>
        attribute.AttributeClass is { Name: "NativeOwnershipAttribute" } marker
        && marker.GetAttributes().Any(generated => generated.AttributeClass?.ToDisplayString() == "System.CodeDom.Compiler.GeneratedCodeAttribute"
            && generated.ConstructorArguments[0].Value is string tool && tool == "Microsoft.Windows.CsWin32"));

    private static bool IsLease(ITypeSymbol? type) => type is not null
        && GetOwnership(type) is { ConstructorArguments.Length: >= 2 } attribute
        && attribute.ConstructorArguments[1].Value is true;

    private static bool IsWithinNameOf(IOperation operation)
    {
        for (IOperation? parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is INameOfOperation)
            {
                return true;
            }
        }

        return false;
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static bool IsUsingLocal(ILocalSymbol local) => local.DeclaringSyntaxReferences.Any(reference =>
        reference.GetSyntax() is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration }
        && ((declaration.Parent is LocalDeclarationStatementSyntax statement && !statement.UsingKeyword.IsKind(SyntaxKind.None))
            || declaration.Parent is UsingStatementSyntax));

    private static bool IsDirectLeaseFactory(IOperation? operation) => operation is not null
        && Unwrap(operation) is IInvocationOperation { TargetMethod.Name: "Lease" } invocation
        && IsLease(invocation.Type);

    private static void AnalyzeLocal(SyntaxNodeAnalysisContext context)
    {
        var declaration = (VariableDeclaratorSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is ILocalSymbol local
            && IsLease(local.Type)
            && (!IsUsingLocal(local) || !IsDirectLeaseFactory(declaration.Initializer is { } initializer
                ? context.SemanticModel.GetOperation(initializer.Value, context.CancellationToken) : null)))
        {
            context.ReportDiagnostic(Diagnostic.Create(LeaseMustBeUsingLocal, declaration.GetLocation()));
        }
    }

    private static void AnalyzeDeclaration(SymbolAnalysisContext context)
    {
        ITypeSymbol? type = context.Symbol switch
        {
            IFieldSymbol field => field.Type,
            IParameterSymbol parameter => parameter.Type,
            IPropertySymbol property => property.Type,
            IMethodSymbol method => method.ReturnType,
            _ => null,
        };
        if (IsLease(type) && !context.Symbol.IsImplicitlyDeclared)
        {
            foreach (Location location in context.Symbol.Locations.Where(location => location.IsInSource))
            {
                context.ReportDiagnostic(Diagnostic.Create(LeaseMustBeUsingLocal, location));
            }
        }
    }

    private static void AnalyzeLeaseCreation(OperationAnalysisContext context)
    {
        if (!IsLease(context.Operation.Type))
        {
            return;
        }

        IOperation operation = context.Operation;
        while (operation.Parent is IConversionOperation conversion)
        {
            operation = conversion;
        }

        if (operation.Parent is not IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator }
            || !IsUsingLocal(declarator.Symbol) || !IsDirectLeaseFactory(operation))
        {
            context.ReportDiagnostic(Diagnostic.Create(LeaseMustBeUsingLocal, context.Operation.Syntax.GetLocation()));
        }
    }

    private static void AnalyzeLeaseReference(OperationAnalysisContext context)
    {
        var reference = (ILocalReferenceOperation)context.Operation;
        if (!IsLease(reference.Type) || IsWithinNameOf(reference))
        {
            return;
        }

        if (reference.Parent is not IPropertyReferenceOperation { Property.Name: "Value" } property || GetOwnership(property.Property) is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(LeaseMustBeUsingLocal, reference.Syntax.GetLocation()));
        }
    }

    private static bool HasScope(IOperation? instance, SyntaxNode usage, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        if (instance is null)
        {
            return false;
        }

        ISymbol? owner = Unwrap(instance) switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null,
        };
        if (owner is null)
        {
            return false;
        }

        foreach (SyntaxNode ancestor in usage.Ancestors())
        {
            if (ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return false;
            }

            if (ancestor is UsingStatementSyntax { Expression: { } expression }
                && SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol, owner))
            {
                return true;
            }

            if (owner is ILocalSymbol local && IsUsingLocal(local))
            {
                foreach (SyntaxReference reference in local.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration }
                        && ((declaration.Parent is LocalDeclarationStatementSyntax { Parent: { } scope } && scope == ancestor)
                            || (declaration.Parent is UsingStatementSyntax statement && statement == ancestor)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsReleaseImplementation(IOperation? instance, ISymbol containingSymbol) =>
        instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }
        && containingSymbol is IMethodSymbol { Name: "ReleaseHandle", IsOverride: true, Parameters.Length: 0 };

    private static void AnalyzeValue(OperationAnalysisContext context)
    {
        var property = (IPropertyReferenceOperation)context.Operation;
        if (GetOwnership(property.Property) is not { } ownership || IsWithinNameOf(property))
        {
            return;
        }

        bool releasing = IsReleaseImplementation(property.Instance, context.ContainingSymbol);
        if (!releasing && (property.SemanticModel is not { } semanticModel || !HasScope(property.Instance, property.Syntax, semanticModel, context.CancellationToken)))
        {
            context.ReportDiagnostic(Diagnostic.Create(ValueNeedsScope, property.Syntax.GetLocation()));
        }

        IOperation use = property;
        while (true)
        {
            if (use.Parent is IConversionOperation or IParenthesizedOperation
                || (use.Parent is IFieldReferenceOperation field && field.Instance == use)
                || (use.Parent is IPropertyReferenceOperation member && member.Instance == use))
            {
                use = use.Parent;
                if (use.Type?.SpecialType is SpecialType.System_Boolean or SpecialType.System_String)
                {
                    return;
                }
            }
            else
            {
                break;
            }
        }

        if (use.Parent is IArgumentOperation { Parent: IInvocationOperation call } argument)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                context.ReportDiagnostic(Diagnostic.Create(ValueMustNotEscape, use.Syntax.GetLocation()));
            }

            string? releaseMethod = ownership.ConstructorArguments[0].Value as string;
            if (!releasing && (call.TargetMethod.Name == releaseMethod || call.TargetMethod.GetDllImportData()?.EntryPointName == releaseMethod))
            {
                context.ReportDiagnostic(Diagnostic.Create(ValueMustNotRelease, use.Syntax.GetLocation(), releaseMethod));
            }

            return;
        }

        if ((use.Parent is IInvocationOperation { Type.SpecialType: SpecialType.System_String } invocation && invocation.Instance == use)
            || use.Parent is IBinaryOperation { Type.SpecialType: SpecialType.System_Boolean }
            || use.Parent is IIsPatternOperation)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(ValueMustNotEscape, use.Syntax.GetLocation()));
    }
}
