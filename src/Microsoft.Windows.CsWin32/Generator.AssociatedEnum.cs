// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    private readonly Dictionary<(string Name, string Namespace), HandleTypeHandleInfo> associatedEnums = new();
    private Dictionary<ParameterHandle, TypeDefinitionHandle>? associatedEnumParameterOwners;

    /// <summary>
    /// Gets the exact integral storage type of an associated enum.
    /// </summary>
    /// <param name="enumType">The resolved enum definition.</param>
    /// <returns>The enum's underlying primitive type.</returns>
    internal static PrimitiveTypeCode GetEnumUnderlyingType(HandleTypeHandleInfo enumType)
    {
        TypeDefinition definition = enumType.Reader.GetTypeDefinition((TypeDefinitionHandle)enumType.Handle);
        enumType.Generator.GetBaseTypeInfo(definition, out StringHandle baseName, out StringHandle baseNamespace);
        if (!enumType.Reader.StringComparer.Equals(baseName, nameof(Enum)) || !enumType.Reader.StringComparer.Equals(baseNamespace, nameof(System)))
        {
            throw new GenerationFailedException($"AssociatedEnum \"{enumType}\" does not refer to an enum.");
        }

        FieldDefinition storage = definition.GetFields().Select(enumType.Reader.GetFieldDefinition).Single(field => !field.Attributes.HasFlag(FieldAttributes.Static));
        if (storage.DecodeSignature(enumType.Generator.SignatureHandleProvider, null) is not PrimitiveTypeHandleInfo underlying
            || underlying.PrimitiveTypeCode is not (PrimitiveTypeCode.SByte or PrimitiveTypeCode.Byte or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16
                or PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32 or PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64))
        {
            throw new GenerationFailedException($"AssociatedEnum \"{enumType}\" has unsupported storage.");
        }

        return underlying.PrimitiveTypeCode;
    }

    /// <summary>
    /// Resolves an enum association within the declaring namespace and the metadata reference closure.
    /// </summary>
    /// <param name="customAttributeHandles">The attributes on the native value.</param>
    /// <returns>The associated enum, or null when no association is present.</returns>
    internal HandleTypeHandleInfo? FindAssociatedEnum(CustomAttributeHandleCollection? customAttributeHandles)
    {
        if (this.FindAttribute(customAttributeHandles, InteropDecorationNamespace, AssociatedEnumAttribute) is not CustomAttribute attribute)
        {
            return null;
        }

        if (attribute.Parent.Kind == HandleKind.FieldDefinition
            && this.FindAttribute(customAttributeHandles, InteropDecorationNamespace, NativeBitfieldAttribute) is not null)
        {
            throw new GenerationFailedException("AssociatedEnum on a NativeBitfield backing field is not supported.");
        }

        CustomAttributeValue<TypeSyntax> arguments = attribute.DecodeValue(CustomAttributeTypeProvider.Instance);
        if (arguments.FixedArguments.Length != 1 || arguments.FixedArguments[0].Value is not string name || string.IsNullOrWhiteSpace(name))
        {
            throw new GenerationFailedException("AssociatedEnum requires a nonempty enum name.");
        }

        string scope = this.GetAssociatedEnumNamespace(attribute.Parent);
        if (this.associatedEnums.TryGetValue((name, scope), out HandleTypeHandleInfo? cached))
        {
            return cached;
        }

        TrySplitPossiblyQualifiedName(name, out string? enumNamespace, out string enumName);
        List<HandleTypeHandleInfo> matches = new();
        bool incompatiblePlatform = false;
        foreach (Generator generator in this.GetAssociatedEnumReferenceClosure())
        {
            foreach (NamespaceMetadata ns in generator.GetNamespacesToSearch(enumNamespace))
            {
                if (ns.Types.TryGetValue(enumName, out TypeDefinitionHandle handle))
                {
                    matches.Add(new HandleTypeHandleInfo(generator, generator.Reader, handle));
                }

                incompatiblePlatform |= ns.TypesForOtherPlatform.Contains(enumName);
            }
        }

        if (enumNamespace is null)
        {
            List<HandleTypeHandleInfo> localMatches = matches.Where(match =>
                match.Reader.StringComparer.Equals(match.Reader.GetTypeDefinition((TypeDefinitionHandle)match.Handle).Namespace, scope)).ToList();
            if (localMatches.Count > 0)
            {
                matches = localMatches;
            }
        }

        if (matches.Count != 1)
        {
            if (matches.Count == 0 && incompatiblePlatform)
            {
                throw new PlatformIncompatibleException($"AssociatedEnum \"{name}\" is not available for the target platform.");
            }

            throw new GenerationFailedException(matches.Count == 0
                ? $"AssociatedEnum \"{name}\" was not found in the metadata reference closure."
                : $"AssociatedEnum \"{name}\" is ambiguous. Use a namespace-qualified enum name.");
        }

        HandleTypeHandleInfo result = matches[0];
        GetEnumUnderlyingType(result);
        this.associatedEnums.Add((name, scope), result);
        return result;
    }

    /// <summary>
    /// Rejects associations that would reinterpret incompatible unmanaged element storage.
    /// </summary>
    /// <param name="elementType">The native element type.</param>
    /// <param name="attributes">The attributes describing the association.</param>
    /// <param name="allowMarshaling">Whether a single element can be converted by the marshaler.</param>
    internal void ValidateAssociatedEnumElementType(TypeHandleInfo elementType, CustomAttributeHandleCollection? attributes, bool allowMarshaling)
    {
        if (this.FindAssociatedEnum(attributes) is not HandleTypeHandleInfo enumType)
        {
            return;
        }

        while (elementType is ITypeHandleContainer container)
        {
            allowMarshaling = false;
            elementType = container.ElementType;
        }

        if (elementType is PrimitiveTypeHandleInfo primitive && primitive.PrimitiveTypeCode != GetEnumUnderlyingType(enumType) && !allowMarshaling)
        {
            throw new GenerationFailedException($"AssociatedEnum \"{enumType}\" cannot reinterpret native {primitive} storage. An unmanaged enum element must have the same width and signedness.");
        }
    }

    /// <summary>
    /// Requests a converting marshaler in the metadata generator that owns the enum.
    /// </summary>
    /// <param name="enumType">The associated enum.</param>
    /// <param name="unmanagedType">The native integral storage.</param>
    /// <returns>The qualified marshaler name.</returns>
    internal string RequestAssociatedEnumMarshaller(HandleTypeHandleInfo enumType, UnmanagedType unmanagedType)
    {
        TypeDefinition definition = enumType.Reader.GetTypeDefinition((TypeDefinitionHandle)enumType.Handle);
        string qualifiedName = $"{enumType.Reader.GetString(definition.Namespace)}.{enumType.Reader.GetString(definition.Name)}";
        string? result = null;
        enumType.Generator.volatileCode.GenerationTransaction(() => result = enumType.Generator.RequestCustomEnumMarshaller(qualifiedName, unmanagedType));
        return result!;
    }

    private MethodDeclarationSyntax? DeclareAssociatedEnumOverload(MethodDefinition method, MethodDeclarationSyntax nativeMethod, bool avoidWinmdRootAlias)
    {
        MethodSignature<TypeHandleInfo> signature = method.DecodeSignature(this.SignatureHandleProvider, null);
        ParameterListSyntax parameters = nativeMethod.ParameterList;
        ArgumentSyntax[] arguments = parameters.Parameters.Select(parameter => Argument(IdentifierName(parameter.Identifier))
            .WithRefKindKeyword(parameter.Modifiers.FirstOrDefault(modifier => modifier.Kind() is SyntaxKind.InKeyword or SyntaxKind.OutKeyword or SyntaxKind.RefKeyword))).ToArray();
        bool changed = false;
        TypeSyntaxSettings settings = this.extensionMethodSignatureTypeSettings with { AvoidWinmdRootAlias = avoidWinmdRootAlias };
        foreach (ParameterHandle handle in method.GetParameters())
        {
            Parameter parameter = this.Reader.GetParameter(handle);
            int index = parameter.SequenceNumber - 1;
            if (index < 0 || signature.ParameterTypes[index] is not PrimitiveTypeHandleInfo
                || nativeMethod.ParameterList.Parameters[index].Type is not PredefinedTypeSyntax nativeType
                || this.FindAssociatedEnum(parameter.GetCustomAttributes()) is not HandleTypeHandleInfo enumType)
            {
                continue;
            }

            TypeSyntax projected = enumType.ToTypeSyntax(settings, GeneratingElement.FriendlyOverload, null).Type;
            parameters = parameters.ReplaceNode(parameters.Parameters[index], parameters.Parameters[index].WithType(projected.WithTrailingTrivia(Space)));
            arguments[index] = arguments[index].WithExpression(UncheckedExpression(CastExpression(nativeType.WithoutTrivia(), arguments[index].Expression)));
            changed = true;
        }

        TypeSyntax returnType = nativeMethod.ReturnType;
        ExpressionSyntax invocation = InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName(nativeMethod.Identifier)), [.. arguments]);
        if (signature.ReturnType is PrimitiveTypeHandleInfo && returnType is PredefinedTypeSyntax
            && this.FindAssociatedEnum(this.GetReturnTypeCustomAttributes(method)) is HandleTypeHandleInfo returnEnum)
        {
            returnType = returnEnum.ToTypeSyntax(settings, GeneratingElement.FriendlyOverload, null).Type;
            invocation = UncheckedExpression(CastExpression(returnType, invocation));
        }
        else if (!changed)
        {
            return null;
        }

        return nativeMethod.WithAttributeLists(default)
            .WithIdentifier(changed ? nativeMethod.Identifier : Identifier(nativeMethod.Identifier.ValueText + "_Enum"))
            .WithParameterList(parameters)
            .WithReturnType(returnType)
            .WithBody(Block(IsVoid(returnType) ? ExpressionStatement(invocation) : ReturnStatement(invocation)))
            .WithExpressionBody(null)
            .WithSemicolonToken(default);
    }

    private IEnumerable<Generator> GetAssociatedEnumReferenceClosure()
    {
        HashSet<Generator> visited = new();
        Queue<Generator> pending = new();
        pending.Enqueue(this);
        while (pending.Count > 0)
        {
            Generator generator = pending.Dequeue();
            if (!visited.Add(generator))
            {
                continue;
            }

            yield return generator;
            if (this.SuperGenerator is not SuperGenerator super)
            {
                continue;
            }

            foreach (AssemblyReferenceHandle referenceHandle in generator.Reader.AssemblyReferences)
            {
                AssemblyReference reference = generator.Reader.GetAssemblyReference(referenceHandle);
                if (super.TryGetGenerator(generator.Reader.GetString(reference.Name), out Generator? referencedGenerator))
                {
                    pending.Enqueue(referencedGenerator);
                }
            }
        }
    }

    private string GetAssociatedEnumNamespace(EntityHandle owner)
    {
        TypeDefinitionHandle declaringType;
        if (owner.Kind == HandleKind.FieldDefinition)
        {
            declaringType = this.Reader.GetFieldDefinition((FieldDefinitionHandle)owner).GetDeclaringType();
        }
        else if (owner.Kind == HandleKind.Parameter)
        {
            if (this.associatedEnumParameterOwners is null)
            {
                this.associatedEnumParameterOwners = new();
                foreach (MethodDefinitionHandle methodHandle in this.Reader.MethodDefinitions)
                {
                    MethodDefinition method = this.Reader.GetMethodDefinition(methodHandle);
                    foreach (ParameterHandle parameter in method.GetParameters())
                    {
                        this.associatedEnumParameterOwners.Add(parameter, method.GetDeclaringType());
                    }
                }
            }

            declaringType = this.associatedEnumParameterOwners[(ParameterHandle)owner];
        }
        else
        {
            throw new GenerationFailedException("AssociatedEnum is only supported on fields, parameters and return values.");
        }

        return this.GetNamespaceForPossiblyNestedType(this.Reader.GetTypeDefinition(declaringType));
    }
}
