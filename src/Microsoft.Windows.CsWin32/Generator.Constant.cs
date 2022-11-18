// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    /// <summary>
    /// Generates a projection of all constants.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public void GenerateAllConstants(CancellationToken cancellationToken)
    {
        foreach (FieldDefinitionHandle fieldDefHandle in this.MetadataIndex.Apis.SelectMany(api => this.Reader.GetTypeDefinition(api).GetFields()))
        {
            cancellationToken.ThrowIfCancellationRequested();

            FieldDefinition fieldDef = this.Reader.GetFieldDefinition(fieldDefHandle);
            if (this.IsCompatibleWithPlatform(fieldDef.GetCustomAttributes()))
            {
                try
                {
                    this.volatileCode.GenerationTransaction(delegate
                    {
                        this.RequestConstant(fieldDefHandle);
                    });
                }
                catch (GenerationFailedException ex) when (IsPlatformCompatibleException(ex))
                {
                    // Something transitively required for this field is not available for this platform, so skip this method.
                }
            }
        }
    }

    /// <summary>
    /// Generates code for all constants with a common prefix.
    /// </summary>
    /// <param name="constantNameWithTrailingWildcard">The prefix, including a trailing <c>*</c>. A qualifying namespace is allowed.</param>
    /// <returns><see langword="true" /> if at least one constant matched the prefix and was generated; otherwise <see langword="false" />.</returns>
    public bool TryGenerateConstants(string constantNameWithTrailingWildcard)
    {
        if (constantNameWithTrailingWildcard is null)
        {
            throw new ArgumentNullException(nameof(constantNameWithTrailingWildcard));
        }

        if (constantNameWithTrailingWildcard.Length < 2 || constantNameWithTrailingWildcard[constantNameWithTrailingWildcard.Length - 1] != '*')
        {
            throw new ArgumentException("A name with a wildcard ending is expected.", nameof(constantNameWithTrailingWildcard));
        }

        TrySplitPossiblyQualifiedName(constantNameWithTrailingWildcard, out string? constantNamespace, out string constantName);
        string prefix = constantName.Substring(0, constantName.Length - 1);
        IEnumerable<NamespaceMetadata>? namespaces = this.GetNamespacesToSearch(constantNamespace);
        IEnumerable<FieldDefinitionHandle>? matchingFieldHandles = from ns in namespaces
                                                                   from field in ns.Fields
                                                                   where field.Key.StartsWith(prefix, StringComparison.Ordinal)
                                                                   select field.Value;

        bool anyMatch = false;
        foreach (FieldDefinitionHandle fieldHandle in matchingFieldHandles)
        {
            FieldDefinition field = this.Reader.GetFieldDefinition(fieldHandle);
            if (this.IsCompatibleWithPlatform(field.GetCustomAttributes()))
            {
                try
                {
                    this.volatileCode.GenerationTransaction(delegate
                    {
                        this.RequestConstant(fieldHandle);
                    });
                    anyMatch = true;
                }
                catch (GenerationFailedException ex) when (IsPlatformCompatibleException(ex))
                {
                    // Something transitively required for this API is not available for this platform, so skip this method.
                }
            }
        }

        return anyMatch;
    }

    /// <summary>
    /// Generate code for the named constant, if it is recognized.
    /// </summary>
    /// <param name="possiblyQualifiedName">The name of the constant, optionally qualified with a namespace.</param>
    /// <param name="preciseApi">Receives the canonical API names that <paramref name="possiblyQualifiedName"/> matched on.</param>
    /// <returns><see langword="true"/> if a match was found and the constant generated; otherwise <see langword="false"/>.</returns>
    public bool TryGenerateConstant(string possiblyQualifiedName, out IReadOnlyList<string> preciseApi)
    {
        if (possiblyQualifiedName is null)
        {
            throw new ArgumentNullException(nameof(possiblyQualifiedName));
        }

        TrySplitPossiblyQualifiedName(possiblyQualifiedName, out string? constantNamespace, out string constantName);
        var matchingFieldHandles = new List<FieldDefinitionHandle>();
        IEnumerable<NamespaceMetadata>? namespaces = this.GetNamespacesToSearch(constantNamespace);

        foreach (NamespaceMetadata? nsMetadata in namespaces)
        {
            if (nsMetadata.Fields.TryGetValue(constantName, out FieldDefinitionHandle fieldDefHandle))
            {
                matchingFieldHandles.Add(fieldDefHandle);
            }
        }

        if (matchingFieldHandles.Count == 1)
        {
            this.volatileCode.GenerationTransaction(delegate
            {
                this.RequestConstant(matchingFieldHandles[0]);
            });

            FieldDefinition fd = this.Reader.GetFieldDefinition(matchingFieldHandles[0]);
            TypeDefinition td = this.Reader.GetTypeDefinition(fd.GetDeclaringType());
            preciseApi = ImmutableList.Create($"{this.Reader.GetString(td.Namespace)}.{this.Reader.GetString(fd.Name)}");
            return true;
        }
        else if (matchingFieldHandles.Count > 1)
        {
            preciseApi = ImmutableList.CreateRange(
                matchingFieldHandles.Select(h =>
                {
                    FieldDefinition fd = this.Reader.GetFieldDefinition(h);
                    TypeDefinition td = this.Reader.GetTypeDefinition(fd.GetDeclaringType());
                    return $"{this.Reader.GetString(td.Namespace)}.{this.Reader.GetString(fd.Name)}";
                }));
            return false;
        }

        preciseApi = ImmutableList<string>.Empty;
        return false;
    }

    internal void RequestConstant(FieldDefinitionHandle fieldDefHandle)
    {
        this.volatileCode.GenerateConstant(fieldDefHandle, delegate
        {
            FieldDefinition fieldDef = this.Reader.GetFieldDefinition(fieldDefHandle);
            FieldDeclarationSyntax constantDeclaration = this.DeclareConstant(fieldDef);

            TypeHandleInfo fieldTypeInfo = fieldDef.DecodeSignature<TypeHandleInfo, SignatureHandleProvider.IGenericContext?>(SignatureHandleProvider.Instance, null) with { IsConstantField = true };
            TypeDefinitionHandle? fieldType = null;
            if (fieldTypeInfo is HandleTypeHandleInfo handleInfo && this.IsTypeDefStruct(handleInfo) && handleInfo.Handle.Kind == HandleKind.TypeReference)
            {
                TypeReference tr = this.Reader.GetTypeReference((TypeReferenceHandle)handleInfo.Handle);
                string fieldTypeName = this.Reader.GetString(tr.Name);
                if (!TypeDefsThatDoNotNestTheirConstants.Contains(fieldTypeName) && this.TryGetTypeDefHandle(tr, out TypeDefinitionHandle candidate))
                {
                    fieldType = candidate;
                }
            }

            this.volatileCode.AddConstant(fieldDefHandle, constantDeclaration, fieldType);
        });
    }

    private static ObjectCreationExpressionSyntax PropertyKeyValue(CustomAttribute propertyKeyAttribute, TypeSyntax type)
    {
        CustomAttributeValue<TypeSyntax> args = propertyKeyAttribute.DecodeValue(CustomAttributeTypeProvider.Instance);
        uint a = (uint)args.FixedArguments[0].Value!;
        ushort b = (ushort)args.FixedArguments[1].Value!;
        ushort c = (ushort)args.FixedArguments[2].Value!;
        byte d = (byte)args.FixedArguments[3].Value!;
        byte e = (byte)args.FixedArguments[4].Value!;
        byte f = (byte)args.FixedArguments[5].Value!;
        byte g = (byte)args.FixedArguments[6].Value!;
        byte h = (byte)args.FixedArguments[7].Value!;
        byte i = (byte)args.FixedArguments[8].Value!;
        byte j = (byte)args.FixedArguments[9].Value!;
        byte k = (byte)args.FixedArguments[10].Value!;
        uint pid = (uint)args.FixedArguments[11].Value!;

        return ObjectCreationExpression(type).WithInitializer(
            InitializerExpression(SyntaxKind.ObjectInitializerExpression, SeparatedList<ExpressionSyntax>(new[]
            {
                AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, IdentifierName("fmtid"), GuidValue(propertyKeyAttribute)),
                AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, IdentifierName("pid"), LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(pid))),
            })));
    }

    private FieldDeclarationSyntax DeclareConstant(FieldDefinition fieldDef)
    {
        string name = this.Reader.GetString(fieldDef.Name);
        try
        {
            TypeHandleInfo fieldTypeInfo = fieldDef.DecodeSignature(SignatureHandleProvider.Instance, null) with { IsConstantField = true };
            CustomAttributeHandleCollection customAttributes = fieldDef.GetCustomAttributes();
            TypeSyntaxAndMarshaling fieldType = fieldTypeInfo.ToTypeSyntax(this.fieldTypeSettings, customAttributes);
            ExpressionSyntax value =
                fieldDef.GetDefaultValue() is { IsNil: false } constantHandle ? ToExpressionSyntax(this.Reader, constantHandle) :
                this.FindInteropDecorativeAttribute(customAttributes, nameof(GuidAttribute)) is CustomAttribute guidAttribute ? GuidValue(guidAttribute) :
                this.FindInteropDecorativeAttribute(customAttributes, "PropertyKeyAttribute") is CustomAttribute propertyKeyAttribute ? PropertyKeyValue(propertyKeyAttribute, fieldType.Type) :
                throw new NotSupportedException("Unsupported constant: " + name);
            bool requiresUnsafe = false;
            if (fieldType.Type is not PredefinedTypeSyntax && value is not ObjectCreationExpressionSyntax)
            {
                if (fieldTypeInfo is HandleTypeHandleInfo handleFieldTypeInfo && this.IsHandle(handleFieldTypeInfo.Handle, out _))
                {
                    // Cast to IntPtr first, then the actual handle struct.
                    value = CastExpression(fieldType.Type, CastExpression(IntPtrTypeSyntax, ParenthesizedExpression(value)));
                }
                else if (fieldType.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PCSTR" } } })
                {
                    value = CastExpression(fieldType.Type, CastExpression(PointerType(PredefinedType(Token(SyntaxKind.ByteKeyword))), ParenthesizedExpression(value)));
                    requiresUnsafe = true;
                }
                else if (fieldType.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PCWSTR" } } })
                {
                    value = CastExpression(PointerType(PredefinedType(Token(SyntaxKind.CharKeyword))), ParenthesizedExpression(value));
                    requiresUnsafe = true;
                }
                else
                {
                    value = CastExpression(fieldType.Type, ParenthesizedExpression(value));
                }
            }

            SyntaxTokenList modifiers = TokenList(TokenWithSpace(this.Visibility));
            if (this.IsTypeDefStruct(fieldTypeInfo) || value is ObjectCreationExpressionSyntax)
            {
                modifiers = modifiers.Add(TokenWithSpace(SyntaxKind.StaticKeyword)).Add(TokenWithSpace(SyntaxKind.ReadOnlyKeyword));
            }
            else
            {
                modifiers = modifiers.Add(TokenWithSpace(SyntaxKind.ConstKeyword));
            }

            if (requiresUnsafe)
            {
                modifiers = modifiers.Add(TokenWithSpace(SyntaxKind.UnsafeKeyword));
            }

            FieldDeclarationSyntax? result = FieldDeclaration(VariableDeclaration(fieldType.Type).AddVariables(
                VariableDeclarator(Identifier(name)).WithInitializer(EqualsValueClause(value))))
                .WithModifiers(modifiers);
            result = fieldType.AddMarshalAs(result);
            result = this.AddApiDocumentation(result.Declaration.Variables[0].Identifier.ValueText, result);

            return result;
        }
        catch (Exception ex)
        {
            TypeDefinition typeDef = this.Reader.GetTypeDefinition(fieldDef.GetDeclaringType());
            string typeName = this.Reader.GetString(typeDef.Name);
            string? ns = this.Reader.GetString(typeDef.Namespace);
            throw new GenerationFailedException($"Failed creating field: {ns}.{typeName}.{name}", ex);
        }
    }

    private ClassDeclarationSyntax DeclareConstantDefiningClass()
    {
        return ClassDeclaration(this.methodsAndConstantsClassName.Identifier)
            .AddMembers(this.committedCode.TopLevelFields.ToArray())
            .WithModifiers(TokenList(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.PartialKeyword)));
    }
}
