// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    private readonly bool canUseSpan;
    private readonly bool canCallCreateSpan;
    private readonly bool canUseUnsafeAsRef;
    private readonly bool canUseUnsafeNullRef;
    private readonly bool canUseUnmanagedCallersOnlyAttribute;
    private readonly bool unscopedRefAttributePredefined;
    private readonly INamedTypeSymbol? runtimeFeatureClass;
    private readonly bool generateSupportedOSPlatformAttributes;
    private readonly bool generateSupportedOSPlatformAttributesOnInterfaces; // only supported on net6.0 (https://github.com/dotnet/runtime/pull/48838)
    private readonly bool generateDefaultDllImportSearchPathsAttribute;
    private readonly Dictionary<Feature, bool> supportedFeatures = new();

    private void DeclareUnscopedRefAttributeIfNecessary()
    {
        if (this.unscopedRefAttributePredefined)
        {
            return;
        }

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
            AttributeListSyntax usageAttr = AttributeList().AddAttributes(
                Attribute(IdentifierName(nameof(AttributeUsageAttribute))).AddArgumentListArguments(
                    AttributeArgument(CompoundExpression(SyntaxKind.BitwiseOrExpression, uses)),
                    AttributeArgument(LiteralExpression(SyntaxKind.FalseLiteralExpression)).WithNameEquals(NameEquals(IdentifierName("AllowMultiple"))),
                    AttributeArgument(LiteralExpression(SyntaxKind.FalseLiteralExpression)).WithNameEquals(NameEquals(IdentifierName("Inherited")))));
            ClassDeclarationSyntax attrDecl = ClassDeclaration(Identifier("UnscopedRefAttribute"))
                .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(IdentifierName("Attribute")))))
                .AddModifiers(Token(SyntaxKind.InternalKeyword), TokenWithSpace(SyntaxKind.SealedKeyword))
                .AddAttributeLists(usageAttr);
            NamespaceDeclarationSyntax nsDeclaration = NamespaceDeclaration(ParseName("System.Diagnostics.CodeAnalysis"))
                .AddMembers(attrDecl);

            this.volatileCode.AddSpecialType(name, nsDeclaration, topLevel: true);
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
