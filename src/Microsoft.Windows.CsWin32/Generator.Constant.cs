// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    /// <inheritdoc/>
    public void GenerateAllConstants(CancellationToken cancellationToken)
    {
        var fields = this.MetadataIndex.Apis.SelectMany(api => this.Reader.GetTypeDefinition(api).GetFields());
        var sortedFields = fields.OrderBy(fieldDefHandle => this.Reader.GetString(this.Reader.GetFieldDefinition(fieldDefHandle).Name));

        foreach (FieldDefinitionHandle fieldDefHandle in sortedFields)
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
    public bool TryGenerateConstant(string possiblyQualifiedName, out IReadOnlyCollection<string> preciseApi)
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

            TypeHandleInfo fieldTypeInfo = fieldDef.DecodeSignature<TypeHandleInfo, SignatureHandleProvider.IGenericContext?>(this.SignatureHandleProvider, null) with { IsConstantField = true };
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

    private static List<ReadOnlyMemory<char>> SplitConstantArguments(ReadOnlyMemory<char> args)
    {
        List<ReadOnlyMemory<char>> argExpressions = new();

        // Recursively parse the arguments, splitting on commas that are not nested within curly brances.
        int start = 0;
        int depth = 0;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args.Span[i])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    break;
                case ',':
                    if (depth == 0)
                    {
                        ReadOnlyMemory<char> arg = args.Slice(start, i - start);

                        argExpressions.Add(TrimCurlyBraces(arg));
                        start = i + 1;

                        // Trim a leading space if present.
                        if (args.Span[start] == ' ')
                        {
                            start++;
                            i++;
                        }
                    }

                    break;
            }
        }

        if (start < args.Length)
        {
            argExpressions.Add(TrimCurlyBraces(args.Slice(start)));
        }

        ReadOnlyMemory<char> TrimCurlyBraces(ReadOnlyMemory<char> arg)
        {
            return arg.Span[0] == '{' && arg.Span[arg.Length - 1] == '}' ? arg.Slice(1, arg.Length - 2) : arg;
        }

        return argExpressions;
    }

    private ObjectCreationExpressionSyntax? CreateConstantViaCtor(List<ReadOnlyMemory<char>> args, TypeSyntax targetType, TypeDefinition targetTypeDef, out bool unsafeRequired)
    {
        unsafeRequired = false;
        foreach (MethodDefinitionHandle methodDefHandle in targetTypeDef.GetMethods())
        {
            MethodDefinition methodDef = this.Reader.GetMethodDefinition(methodDefHandle);
            if (this.Reader.StringComparer.Equals(methodDef.Name, ".ctor") && methodDef.GetParameters().Count == args.Count)
            {
                MethodSignature<TypeHandleInfo> ctorSignature = methodDef.DecodeSignature(this.SignatureHandleProvider, null);
                var argExpressions = new ArgumentSyntax[args.Count];

                for (int i = 0; i < args.Count; i++)
                {
                    TypeHandleInfo parameterTypeInfo = ctorSignature.ParameterTypes[i];
                    argExpressions[i] = Argument(this.CreateConstant(args[i], parameterTypeInfo, out bool thisRequiresUnsafe));
                    unsafeRequired |= thisRequiresUnsafe;
                    i++;
                }

                return ObjectCreationExpression(targetType, [.. argExpressions]);
            }
        }

        return null;
    }

    private ObjectCreationExpressionSyntax? CreateConstantByField(List<ReadOnlyMemory<char>> args, TypeSyntax targetType, TypeDefinition targetTypeDef, out bool unsafeRequired)
    {
        unsafeRequired = false;
        if (targetTypeDef.GetFields().Count != args.Count)
        {
            return null;
        }

        var fieldAssignmentExpressions = new AssignmentExpressionSyntax[args.Count];
        int i = 0;
        foreach (FieldDefinitionHandle fieldDefHandle in targetTypeDef.GetFields())
        {
            FieldDefinition fieldDef = this.Reader.GetFieldDefinition(fieldDefHandle);
            string fieldName = this.Reader.GetString(fieldDef.Name);
            TypeHandleInfo fieldTypeInfo = fieldDef.DecodeSignature(this.SignatureHandleProvider, null) with { IsConstantField = true };
            fieldAssignmentExpressions[i] = AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                IdentifierName(fieldName),
                this.CreateConstant(args[i], fieldTypeInfo, out bool thisRequiresUnsafe));
            unsafeRequired |= thisRequiresUnsafe;
            i++;
        }

        return ObjectCreationExpression(targetType)
            .WithArgumentList(null)
            .WithInitializer(InitializerExpression(SyntaxKind.ObjectInitializerExpression).AddExpressions(fieldAssignmentExpressions));
    }

    private ExpressionSyntax CreateConstant(ReadOnlyMemory<char> argsAsString, TypeHandleInfo targetType, out bool unsafeRequired)
    {
        unsafeRequired = targetType is PointerTypeHandleInfo;
        return targetType switch
        {
            ArrayTypeHandleInfo { ElementType: PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.Byte } } pointerType => this.CreateByteArrayConstant(argsAsString),
            PrimitiveTypeHandleInfo primitiveType => ToExpressionSyntax(primitiveType.PrimitiveTypeCode, argsAsString),
            HandleTypeHandleInfo handleType => this.CreateConstant(argsAsString, targetType.ToTypeSyntax(this.fieldTypeSettings, GeneratingElement.Constant, null).Type, (TypeReferenceHandle)handleType.Handle, out unsafeRequired),
            PointerTypeHandleInfo pointerType => CastExpression(pointerType.ToTypeSyntax(this.fieldTypeSettings, GeneratingElement.Constant, null).Type, ParenthesizedExpression(ToExpressionSyntax(PrimitiveTypeCode.UInt64, argsAsString))),
            _ => throw new GenerationFailedException($"Unsupported constant type: {targetType}"),
        };
    }

    private ExpressionSyntax CreateConstant(ReadOnlyMemory<char> argsAsString, TypeSyntax targetType, TypeReferenceHandle targetTypeRefHandle, out bool unsafeRequired)
    {
        if (!this.TryGetTypeDefHandle(targetTypeRefHandle, out QualifiedTypeDefinitionHandle targetTypeDefHandle))
        {
            // Special case for System.Guid.
            TypeReference typeRef = this.Reader.GetTypeReference(targetTypeRefHandle);
            if (this.Reader.StringComparer.Equals(typeRef.Name, "Guid"))
            {
                List<ReadOnlyMemory<char>> guidArgs = SplitConstantArguments(argsAsString);
                unsafeRequired = false;
                return this.CreateGuidConstant(guidArgs);
            }

            throw new GenerationFailedException("Unrecognized target type.");
        }

        targetTypeDefHandle.Generator.volatileCode.GenerationTransaction(delegate
        {
            targetTypeDefHandle.Generator.RequestInteropType(targetTypeDefHandle.DefinitionHandle, this.DefaultContext);
        });
        TypeDefinition typeDef = targetTypeDefHandle.Reader.GetTypeDefinition(targetTypeDefHandle.DefinitionHandle);

        List<ReadOnlyMemory<char>> args = SplitConstantArguments(argsAsString);

        ObjectCreationExpressionSyntax? result =
            targetTypeDefHandle.Generator.CreateConstantViaCtor(args, targetType, typeDef, out unsafeRequired) ??
            targetTypeDefHandle.Generator.CreateConstantByField(args, targetType, typeDef, out unsafeRequired);

        return result ?? throw new GenerationFailedException($"Unable to construct constant value given {args.Count} fields or constructor arguments.");
    }

    private ExpressionSyntax CreateByteArrayConstant(ReadOnlyMemory<char> argsAsString)
    {
        List<ReadOnlyMemory<char>> args = SplitConstantArguments(argsAsString);
        TypeSyntax byteTypeSyntax = PredefinedType(Token(SyntaxKind.ByteKeyword));
        return CastExpression(
            MakeReadOnlySpanOfT(byteTypeSyntax),
            ArrayCreationExpression(ArrayType(byteTypeSyntax, [ArrayRankSpecifier()]), InitializerExpression(SyntaxKind.ArrayInitializerExpression, [.. args.Select(b => ToExpressionSyntax(PrimitiveTypeCode.Byte, b))])));
    }

    private ExpressionSyntax CreateGuidConstant(List<ReadOnlyMemory<char>> guidArgs)
    {
        if (guidArgs.Count != 11)
        {
            throw new GenerationFailedException($"Unexpected element count {guidArgs.Count} when constructing a Guid, which requires 11.");
        }

        var ctorArgs = new SyntaxToken[11]
        {
            Literal(uint.Parse(guidArgs[0].ToString(), CultureInfo.InvariantCulture)),
            Literal(ushort.Parse(guidArgs[1].ToString(), CultureInfo.InvariantCulture)),
            Literal(ushort.Parse(guidArgs[2].ToString(), CultureInfo.InvariantCulture)),
            Literal(byte.Parse(guidArgs[3].ToString(), CultureInfo.InvariantCulture)),
            Literal(byte.Parse(guidArgs[4].ToString(), CultureInfo.InvariantCulture)),
            Literal(byte.Parse(guidArgs[5].ToString(), CultureInfo.InvariantCulture)),
            Literal(byte.Parse(guidArgs[6].ToString(), CultureInfo.InvariantCulture)),
            Literal(byte.Parse(guidArgs[7].ToString(), CultureInfo.InvariantCulture)),
            Literal(byte.Parse(guidArgs[8].ToString(), CultureInfo.InvariantCulture)),
            Literal(byte.Parse(guidArgs[9].ToString(), CultureInfo.InvariantCulture)),
            Literal(byte.Parse(guidArgs[10].ToString(), CultureInfo.InvariantCulture)),
        };

        return ObjectCreationExpression(GuidTypeSyntax, [.. ctorArgs.Select(t => Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, t)))]);
    }

    private ExpressionSyntax CreateConstant(CustomAttribute constantAttribute, TypeHandleInfo targetType, out bool unsafeRequired)
    {
        TypeReferenceHandle targetTypeRefHandle = (TypeReferenceHandle)((HandleTypeHandleInfo)targetType).Handle;
        CustomAttributeValue<TypeSyntax> args = constantAttribute.DecodeValue(CustomAttributeTypeProvider.Instance);
        return this.CreateConstant(
            ((string)args.FixedArguments[0].Value!).AsMemory(),
            targetType.ToTypeSyntax(this.fieldTypeSettings, GeneratingElement.Constant, null).Type,
            targetTypeRefHandle,
            out unsafeRequired);
    }

    private FieldDeclarationSyntax DeclareConstant(FieldDefinition fieldDef)
    {
        string name = this.Reader.GetString(fieldDef.Name);
        try
        {
            TypeHandleInfo fieldTypeInfo = fieldDef.DecodeSignature(this.SignatureHandleProvider, null) with { IsConstantField = true };
            CustomAttributeHandleCollection customAttributes = fieldDef.GetCustomAttributes();
            TypeSyntaxAndMarshaling fieldType = fieldTypeInfo.ToTypeSyntax(this.fieldTypeSettings, GeneratingElement.Constant, customAttributes.QualifyWith(this));
            bool requiresUnsafe = false;
            ExpressionSyntax value =
                fieldDef.GetDefaultValue() is { IsNil: false } constantHandle ? ToExpressionSyntax(this.Reader, constantHandle) :
                this.FindInteropDecorativeAttribute(customAttributes, nameof(GuidAttribute)) is CustomAttribute guidAttribute ? GuidValue(guidAttribute) :
                this.FindInteropDecorativeAttribute(customAttributes, "ConstantAttribute") is CustomAttribute constantAttribute ? this.CreateConstant(constantAttribute, fieldTypeInfo, out requiresUnsafe) :
                throw new NotSupportedException("Unsupported constant: " + name);
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

            SyntaxTokenList modifiers = [TokenWithSpace(this.Visibility)];
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

            FieldDeclarationSyntax? result = FieldDeclaration(modifiers, VariableDeclaration(fieldType.Type, [VariableDeclarator(Identifier(name), EqualsValueClause(value))]));
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
        return ClassDeclaration(this.methodsAndConstantsClassName.Identifier, [.. this.committedCode.TopLevelFields])
            .WithModifiers([TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.PartialKeyword)]);
    }
}
