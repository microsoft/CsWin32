// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    /// <summary>
    /// The name of the C# compiler feature flag (i.e. <c>&lt;Features&gt;</c> / <c>/features:</c>) that opts an
    /// assembly into the updated memory safety rules introduced as a preview feature in C# 15 / .NET 11.
    /// </summary>
    /// <remarks>
    /// There is no MSBuild property nor public Roslyn API for this opt-in while the feature is in preview,
    /// so the compiler feature flag is the only signal available to us.
    /// </remarks>
    internal const string UpdatedMemorySafetyRulesFeature = "updated-memory-safety-rules";

    private readonly bool canUseUnscopedRef;
    private readonly bool canUseSpan;
    private readonly bool canCallCreateSpan;
    private readonly bool canUseUnsafeAsRef;
    private readonly bool canUseUnsafeAdd;
    private readonly bool canUseUnsafeNullRef;
    private readonly bool canUseUnsafeSkipInit;
    private readonly bool canUseUnmanagedCallersOnlyAttribute;
    private readonly bool canUseSetLastPInvokeError;
    private readonly bool canUseIPropertyValue;
    private readonly bool canDeclareProperties;
    private readonly bool useSourceGenerators;
    private readonly bool canMarshalNativeDelegateParams;
    private readonly bool overloadResolutionPriorityAttributePredefined;
    private readonly bool unscopedRefAttributePredefined;
    private readonly bool canUseComVariant;
    private readonly bool canUseMemberFunctionCallingConvention;
    private readonly bool canUseMarshalInitHandle;
    private readonly bool canUseCsWinRT;
    private readonly bool canUseCustomMarshaller;
    private readonly bool useUpdatedMemorySafetyRules;
    private readonly INamedTypeSymbol? runtimeFeatureClass;
    private readonly bool generateSupportedOSPlatformAttributes;
    private readonly bool generateSupportedOSPlatformAttributesOnInterfaces; // only supported on net6.0 (https://github.com/dotnet/runtime/pull/48838)
    private readonly bool generateDefaultDllImportSearchPathsAttribute;
    private readonly Dictionary<Feature, bool> supportedFeatures = new();

    internal bool UseSourceGenerators => this.useSourceGenerators;

    internal bool CanUseIPropertyValue => this.canUseIPropertyValue;

    internal bool CanUseComVariant => this.canUseComVariant;

    /// <summary>
    /// Determines whether the given parse options opt into the updated memory safety rules.
    /// </summary>
    /// <param name="parseOptions">The parse options for the compilation, if known.</param>
    /// <returns><see langword="true"/> if the updated memory safety rules are enabled.</returns>
    internal static bool IsUpdatedMemorySafetyRulesEnabled(ParseOptions? parseOptions)
    {
        if (parseOptions?.Features is not { Count: > 0 } features)
        {
            return false;
        }

        foreach (KeyValuePair<string, string> feature in features)
        {
            if (string.Equals(feature.Key, UpdatedMemorySafetyRulesFeature, StringComparison.OrdinalIgnoreCase))
            {
                // Roslyn feature flags are enabled by mere presence. An explicit "false" value is honored
                // as a courtesy so that a project can turn the flag back off in a leaf project file.
                return !string.Equals(feature.Value, "false", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private void DeclareOverloadResolutionPriorityAttributeIfNecessary()
    {
        // This attribute may only be applied for C# 13 and later, or else C# errors out.
        if (this.LanguageVersion < (LanguageVersion)1300)
        {
            throw new GenerationFailedException("The OverloadResolutionPriorityAttribute requires C# 13 or later.");
        }

        if (this.overloadResolutionPriorityAttributePredefined)
        {
            return;
        }

        // Always generate these in the context of the most common metadata so we don't emit it more than once.
        if (!this.IsWin32Sdk)
        {
            this.MainGenerator.volatileCode.GenerationTransaction(() => this.MainGenerator.DeclareOverloadResolutionPriorityAttributeIfNecessary());
            return;
        }

        const string name = "OverloadResolutionPriorityAttribute";
        this.volatileCode.GenerateSpecialType(name, delegate
        {
            // This is a polyfill attribute, so never promote visibility to public.
            if (!TryFetchTemplate(name, this, out CompilationUnitSyntax? compilationUnit))
            {
                throw new GenerationFailedException($"Failed to retrieve template: {name}");
            }

            MemberDeclarationSyntax templateNamespace = compilationUnit.Members.Single();
            this.volatileCode.AddSpecialType(name, templateNamespace, topLevel: true);
        });
    }

    private void DeclareUnscopedRefAttributeIfNecessary()
    {
        if (this.unscopedRefAttributePredefined)
        {
            return;
        }

        // Always generate these in the context of the most common metadata so we don't emit it more than once.
        if (!this.IsWin32Sdk)
        {
            this.MainGenerator.volatileCode.GenerationTransaction(() => this.MainGenerator.DeclareUnscopedRefAttributeIfNecessary());
            return;
        }

        const string name = "UnscopedRefAttribute";
        this.volatileCode.GenerateSpecialType(name, delegate
        {
            ExpressionSyntax[] uses = new[]
            {
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(AttributeTargets)), IdentifierName(nameof(AttributeTargets.Method))),
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(AttributeTargets)), IdentifierName(nameof(AttributeTargets.Property))),
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(AttributeTargets)), IdentifierName(nameof(AttributeTargets.Parameter))),
            };
            AttributeListSyntax usageAttr = AttributeList(
                Attribute(IdentifierName(nameof(AttributeUsageAttribute))).AddArgumentListArguments(
                    AttributeArgument(CompoundExpression(SyntaxKind.BitwiseOrExpression, uses)),
                    AttributeArgument(LiteralExpression(SyntaxKind.FalseLiteralExpression)).WithNameEquals(NameEquals(IdentifierName("AllowMultiple"))),
                    AttributeArgument(LiteralExpression(SyntaxKind.FalseLiteralExpression)).WithNameEquals(NameEquals(IdentifierName("Inherited")))));
            ClassDeclarationSyntax attrDecl = ClassDeclaration(Identifier("UnscopedRefAttribute"))
                .WithBaseList(BaseList(SimpleBaseType(IdentifierName("Attribute"))))
                .AddModifiers(Token(SyntaxKind.InternalKeyword), TokenWithSpace(SyntaxKind.SealedKeyword))
                .AddAttributeLists(usageAttr);
            NamespaceDeclarationSyntax nsDeclaration = NamespaceDeclaration(ParseName("System.Diagnostics.CodeAnalysis"), [attrDecl]);

            this.volatileCode.AddSpecialType(name, nsDeclaration, topLevel: true);
        });
    }

    private void DeclareCharSetWorkaroundIfNecessary()
    {
        const string name = "CharSet_Workaround";
        this.volatileCode.GenerateSpecialType(name, delegate
        {
            // This is a polyfill attribute, so never promote visibility to public.
            if (!TryFetchTemplate(name, this, out CompilationUnitSyntax? compilationUnit))
            {
                throw new GenerationFailedException($"Failed to retrieve template: {name}");
            }

            MemberDeclarationSyntax templateNamespace = compilationUnit.Members.Single();

            // templateNamespace is System.Runtime.InteropServices, nest it within this generator's root namespace
            templateNamespace = NamespaceDeclaration(ParseName(this.Namespace), [templateNamespace]);
            this.volatileCode.AddSpecialType(name, templateNamespace, topLevel: true);
        });
    }

    private bool IsFeatureAvailable(Feature feature)
    {
        if (this.supportedFeatures.TryGetValue(feature, out bool result))
        {
            return result;
        }

        // A feature requires a member on the class, and we ignore features that have the `[RequiresPreviewFeatures]` attribute on them.
        bool IsRuntimeFeatureSupported(string name) => this.runtimeFeatureClass?.GetMembers(name).FirstOrDefault()?.GetAttributes().IsEmpty is true;

        result = feature switch
        {
            Feature.InterfaceStaticMembers => (int)this.LanguageVersion >= 1100 && IsRuntimeFeatureSupported("VirtualStaticsInInterfaces"),
            _ => throw new NotImplementedException(),
        };

        this.supportedFeatures.Add(feature, result);
        return result;
    }
}
