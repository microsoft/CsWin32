// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    /// <summary>
    /// Finds contextual cleanup information, falling back to a typedef's legacy default.
    /// </summary>
    /// <param name="valueType">The native value type.</param>
    /// <param name="attributes">The return or parameter annotations.</param>
    /// <param name="releaseMethod">Receives the cleanup function name.</param>
    /// <returns>Whether cleanup information is available.</returns>
    internal bool TryGetResourceReleaseMethod(TypeHandleInfo valueType, CustomAttributeHandleCollection? attributes, [NotNullWhen(true)] out string? releaseMethod)
    {
        if (this.TryGetContextualReleaseMethod(attributes, out releaseMethod))
        {
            return true;
        }

        if (valueType is HandleTypeHandleInfo nativeType)
        {
            return nativeType.Generator.TryGetHandleReleaseMethod(nativeType.Handle, null, out releaseMethod);
        }

        releaseMethod = null;
        return false;
    }

    private static string GetNativeValueName(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax qualified => GetNativeValueName(qualified.Right),
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        PointerTypeSyntax pointer => GetNativeValueName(pointer.ElementType) + "Pointer",
        PredefinedTypeSyntax predefined => predefined.Keyword.ValueText,
        _ => throw new GenerationFailedException($"Unsupported native resource type: {type}."),
    };

    private static bool IsSupportedCleanupReturn(TypeSyntax type, string method) => type switch
    {
        PredefinedTypeSyntax predefined => predefined.Keyword.Kind() is SyntaxKind.BoolKeyword or SyntaxKind.IntKeyword or SyntaxKind.UIntKeyword or SyntaxKind.ByteKeyword or SyntaxKind.VoidKeyword,
        QualifiedNameSyntax { Right: IdentifierNameSyntax name } => name.Identifier.ValueText is "BOOL" or "NTSTATUS" or "HRESULT" or "WIN32_ERROR" or "CONFIGRET" or "HGLOBAL" or "HLOCAL"
            || (name.Identifier.ValueText == "LRESULT" && method == "ICClose"),
        PointerTypeSyntax { ElementType: PredefinedTypeSyntax predefined } => predefined.Keyword.IsKind(SyntaxKind.VoidKeyword) && method == "FreeSid",
        _ => false,
    };

    private bool TryGetContextualReleaseMethod(CustomAttributeHandleCollection? attributes, [NotNullWhen(true)] out string? releaseMethod)
    {
        if ((this.FindAttribute(attributes, InteropDecorationNamespace, RAIIFreeAttribute)
            ?? this.FindAttribute(attributes, InteropDecorationNamespace, FreeWithAttribute)) is CustomAttribute cleanup
            && cleanup.DecodeValue(CustomAttributeTypeProvider.Instance).FixedArguments[0].Value is string name)
        {
            releaseMethod = name;
            return true;
        }

        releaseMethod = null;
        return false;
    }

    private string GetSafeHandleValueName(TypeHandleInfo nativeType, TypeSyntax syntax)
    {
        string name = GetNativeValueName(syntax);
        if (nativeType is HandleTypeHandleInfo handle
            && handle.Generator.MetadataIndex.MetadataByNamespace.Values.Count(metadata => metadata.Types.ContainsKey(name)) > 1)
        {
            QualifiedTypeDefinition definition = handle.Generator.GetQualifiedTypeDefinition(handle.Handle);
            return definition.Reader.GetString(definition.Definition.Namespace).Replace('.', '_') + "_" + name;
        }

        return name;
    }

    private TypeHandleInfo? GetSafeHandleBackingType(TypeHandleInfo type) =>
        this.TryGetTypeDefFieldType(type, out TypeHandleInfo? field) ? field : type;

    private ExpressionSyntax GetNativeValueFromHandle(ExpressionSyntax handle, TypeHandleInfo backingType)
    {
        TypeSyntax fieldType = backingType.ToTypeSyntax(this.fieldTypeSettings, GeneratingElement.HelperClassMember, null).Type;
        return backingType switch
        {
            PointerTypeHandleInfo => CastExpression(fieldType, handle),
            PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.IntPtr } => handle,
            PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.UIntPtr } => UncheckedExpression(CastExpression(fieldType, CastExpression(IdentifierName("nint"), handle))),
            PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.UInt32 } => CheckedExpression(CastExpression(fieldType, UncheckedExpression(CastExpression(IdentifierName("nuint"), CastExpression(IdentifierName("nint"), handle))))),
            _ => CheckedExpression(CastExpression(fieldType, CastExpression(IdentifierName("nint"), handle))),
        };
    }

    private bool IsCleanupParameterCompatible(TypeHandleInfo value, TypeHandleInfo parameter, string releaseMethod)
    {
        TypeSyntax valueSyntax = value.ToTypeSyntax(this.externSignatureTypeSettings, GeneratingElement.HelperClassMember, null).Type;
        TypeSyntax parameterSyntax = parameter.ToTypeSyntax(this.externSignatureTypeSettings, GeneratingElement.HelperClassMember, null).Type;
        if (valueSyntax.IsEquivalentTo(parameterSyntax))
        {
            return true;
        }

        if (this.GetSafeHandleBackingType(value) is PointerTypeHandleInfo
            && (parameter is PointerTypeHandleInfo { ElementType: PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.Void } }
                || releaseMethod is "LocalFree" or "GlobalFree"))
        {
            return true;
        }

        if (value is HandleTypeHandleInfo valueHandle && parameter is HandleTypeHandleInfo parameterHandle)
        {
            QualifiedTypeDefinition valueDefinition = valueHandle.Generator.GetQualifiedTypeDefinition(valueHandle.Handle);
            QualifiedTypeDefinition parameterDefinition = parameterHandle.Generator.GetQualifiedTypeDefinition(parameterHandle.Handle);
            return valueDefinition.Reader.GetString(valueDefinition.Definition.Namespace) == parameterDefinition.Reader.GetString(parameterDefinition.Definition.Namespace)
                && valueDefinition.Generator.GetAlsoUsableForValues(valueDefinition.Definition).Contains(parameterDefinition.Reader.GetString(parameterDefinition.Definition.Name));
        }

        return false;
    }

    private void AddTypedSafeHandleMembers(List<MemberDeclarationSyntax> members, IdentifierNameSyntax ownerType, TypeHandleInfo nativeType, string releaseMethod, bool hidesBase)
    {
        const string AttributeName = "NativeOwnershipAttribute";
        this.volatileCode.GenerationTransaction(() => this.volatileCode.GenerateSpecialType(AttributeName, () =>
        {
            FetchTemplate(AttributeName, this, out ClassDeclarationSyntax attribute);
            this.volatileCode.AddSpecialType(AttributeName, attribute.AddAttributeLists(AttributeList(GeneratedCodeAttribute)));
        }));

        TypeSyntax valueType = nativeType.ToTypeSyntax(this.externSignatureTypeSettings, GeneratingElement.HelperClassMember, null).Type;
        ExpressionSyntax handle = InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName(nameof(SafeHandle.DangerousGetHandle))));
        TypeHandleInfo backingType = this.GetSafeHandleBackingType(nativeType) ?? throw new GenerationFailedException("Missing native resource representation.");
        ExpressionSyntax nativeValue = this.GetNativeValueFromHandle(handle, backingType);
        if (nativeType is HandleTypeHandleInfo)
        {
            nativeValue = CastExpression(valueType, nativeValue);
        }

        AttributeSyntax ValueAttribute(bool lease = false) => Attribute(ParseName($"global::{this.Namespace}.NativeOwnership"))
            .WithArgumentList(AttributeArgumentList(
                AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(releaseMethod))),
                AttributeArgument(LiteralExpression(lease ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression))));

        PropertyDeclarationSyntax value = PropertyDeclaration(valueType.WithTrailingTrivia(Space), "DangerousValue")
            .AddModifiers(TokenWithSpace(this.Visibility))
            .AddAttributeLists(AttributeList(ValueAttribute()))
            .WithExpressionBody(ArrowExpressionClause(nativeValue))
            .WithSemicolonToken(SemicolonWithLineFeed)
            .WithLeadingTrivia(ParseLeadingTrivia("""
                /// <summary>
                /// Gets the native resource without acquiring a reference to keep it alive.
                /// </summary>
                /// <remarks>
                /// Use this property only within the owner's using scope, or acquire a lease and use its Value instead.
                /// Do not save or return the native value, or use it to release the resource.
                /// </remarks>

                """));
        if (hidesBase)
        {
            value = value.AddModifiers(TokenWithSpace(SyntaxKind.NewKeyword));
        }

        members.Add(value);
        if (this.LanguageVersion < LanguageVersion.CSharp9)
        {
            return;
        }

        FetchTemplate("SafeHandleLease", this, out StructDeclarationSyntax lease);
        lease = lease.ReplaceNodes(
            lease.DescendantNodes().OfType<IdentifierNameSyntax>().Where(identifier => identifier.Identifier.ValueText is "__OwnerType" or "__NativeType"),
            (original, rewritten) => original.Identifier.ValueText == "__OwnerType" ? ownerType.WithTriviaFrom(rewritten) : valueType.WithTriviaFrom(rewritten));
        SyntaxTriviaList leaseTrivia = lease.GetLeadingTrivia();
        lease = lease.WithoutLeadingTrivia().WithModifiers([TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.RefKeyword)])
            .AddAttributeLists(AttributeList(ValueAttribute(lease: true))).WithLeadingTrivia(leaseTrivia);
        PropertyDeclarationSyntax leaseValue = lease.Members.OfType<PropertyDeclarationSyntax>().Single();
        lease = lease.ReplaceNode(leaseValue, leaseValue.WithoutLeadingTrivia().AddAttributeLists(AttributeList(ValueAttribute())).WithLeadingTrivia(leaseValue.GetLeadingTrivia()));

        MethodDeclarationSyntax leaseMethod = MethodDeclaration(IdentifierName("LeaseScope").WithTrailingTrivia(Space), Identifier("Lease"))
            .AddModifiers(TokenWithSpace(this.Visibility))
            .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(IdentifierName("LeaseScope"), [Argument(ThisExpression())])))
            .WithSemicolonToken(SemicolonWithLineFeed)
            .WithLeadingTrivia(ParseLeadingTrivia("""
                /// <summary>
                /// Acquires a reference to this resource until the returned lease's using scope ends.
                /// </summary>
                /// <returns>An allocation-free lease. Declare it as a using local and do not copy it.</returns>

                """));
        if (hidesBase)
        {
            lease = lease.WithModifiers(lease.Modifiers.Insert(1, TokenWithSpace(SyntaxKind.NewKeyword)));
            leaseMethod = leaseMethod.AddModifiers(TokenWithSpace(SyntaxKind.NewKeyword));
        }

        members.Add(leaseMethod);

        // Restore the generated file's disabled context after the struct, outside rewritten brace trivia.
        members.Add(lease.WithTrailingTrivia(ParseLeadingTrivia("\n#nullable disable\n")));
    }
}
