// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    private readonly Dictionary<(string Name, string Namespace), HandleTypeHandleInfo> associatedEnums = new();
    private readonly Dictionary<TypeDefinitionHandle, PrimitiveTypeCode> associatedEnumStorage = new();
    private Dictionary<ParameterHandle, MethodDefinitionHandle>? associatedEnumParameterOwners;

    /// <summary>
    /// Gets an enum's projected storage from its native association sites.
    /// </summary>
    /// <param name="enumType">The resolved enum definition.</param>
    /// <returns>The enum's underlying primitive type.</returns>
    internal static PrimitiveTypeCode GetEnumUnderlyingType(HandleTypeHandleInfo enumType) =>
        enumType.Generator.GetProjectedEnumUnderlyingType((TypeDefinitionHandle)enumType.Handle);

    /// <summary>
    /// Gets the integral storage type declared in the logical enum metadata.
    /// </summary>
    /// <param name="enumType">The resolved enum definition.</param>
    /// <returns>The metadata's enum storage type.</returns>
    internal static PrimitiveTypeCode GetDeclaredEnumUnderlyingType(HandleTypeHandleInfo enumType)
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
    /// Gets the associated enum syntax without treating its logical declaration as a native use site.
    /// </summary>
    /// <param name="enumType">The associated enum.</param>
    /// <param name="settings">The projection settings.</param>
    /// <param name="forElement">The generated element.</param>
    /// <returns>The projected enum type.</returns>
    internal static TypeSyntax GetAssociatedEnumTypeSyntax(HandleTypeHandleInfo enumType, TypeSyntaxSettings settings, GeneratingElement forElement) =>
        (enumType with { IsAssociatedEnumProjection = true }).ToTypeSyntax(settings, forElement, null).Type;

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

        return this.ResolveAssociatedEnum(GetAssociatedEnumName(attribute), this.GetAssociatedEnumNamespace(attribute.Parent));
    }

    /// <summary>
    /// Validates an associated enum's native element storage before projection.
    /// </summary>
    /// <param name="elementType">The native element type.</param>
    /// <param name="attributes">The attributes describing the association.</param>
    internal void ValidateAssociatedEnumElementType(TypeHandleInfo elementType, CustomAttributeHandleCollection? attributes)
    {
        if (this.FindAssociatedEnum(attributes) is not HandleTypeHandleInfo enumType)
        {
            return;
        }

        while (elementType is ITypeHandleContainer container)
        {
            elementType = container.ElementType;
        }

        if (elementType is PrimitiveTypeHandleInfo primitive)
        {
            this.ValidateAssociatedEnumStorage(enumType, primitive.PrimitiveTypeCode);
        }
    }

    /// <summary>
    /// Requires an enum projection to match the native use site's width and signedness.
    /// </summary>
    /// <param name="enumType">The associated enum.</param>
    /// <param name="nativeType">The native use site's primitive type.</param>
    internal void ValidateAssociatedEnumStorage(HandleTypeHandleInfo enumType, PrimitiveTypeCode nativeType)
    {
        if (this.RequiresAssociatedEnumConversion(enumType, nativeType))
        {
            throw new GenerationFailedException($"AssociatedEnum \"{enumType}\" has conflicting native storage at this {nativeType} use site.");
        }
    }

    /// <summary>
    /// Determines whether real native enum signatures require a scalar adapter instead of a direct projection.
    /// </summary>
    /// <param name="enumType">The associated enum.</param>
    /// <param name="nativeType">The native use site's primitive type.</param>
    /// <returns>Whether the existing native enum and annotated scalar have different storage.</returns>
    internal bool RequiresAssociatedEnumConversion(HandleTypeHandleInfo enumType, PrimitiveTypeCode nativeType)
    {
        if (!IsEnumStorageType(nativeType))
        {
            throw new GenerationFailedException($"AssociatedEnum \"{enumType}\" requires integral native storage, not {nativeType}.");
        }

        return GetEnumUnderlyingType(enumType) != nativeType;
    }

    private static bool IsEnumStorageType(PrimitiveTypeCode type) =>
        type is PrimitiveTypeCode.SByte or PrimitiveTypeCode.Byte or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16
            or PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32 or PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64;

    private static string GetAssociatedEnumName(CustomAttribute attribute)
    {
        CustomAttributeValue<TypeSyntax> arguments = attribute.DecodeValue(CustomAttributeTypeProvider.Instance);
        if (arguments.FixedArguments.Length != 1 || arguments.FixedArguments[0].Value is not string name || string.IsNullOrWhiteSpace(name))
        {
            throw new GenerationFailedException("AssociatedEnum requires a nonempty enum name.");
        }

        return name;
    }

    private HandleTypeHandleInfo ResolveAssociatedEnum(string name, string scope)
    {
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
        GetDeclaredEnumUnderlyingType(result);
        this.associatedEnums.Add((name, scope), result);
        return result;
    }

    private HandleTypeHandleInfo? FindStoragePreservingEnumProperty(TypeHandleInfo nativeType, CustomAttributeHandleCollection attributes) =>
        nativeType is PrimitiveTypeHandleInfo primitive && IsEnumStorageType(primitive.PrimitiveTypeCode)
            && this.FindAssociatedEnum(attributes) is HandleTypeHandleInfo enumType && GetEnumUnderlyingType(enumType) != primitive.PrimitiveTypeCode
            ? enumType : null;

    private PrimitiveTypeCode GetProjectedEnumUnderlyingType(TypeDefinitionHandle enumHandle)
    {
        if (this.associatedEnumStorage.TryGetValue(enumHandle, out PrimitiveTypeCode cached))
        {
            return cached;
        }

        HandleTypeHandleInfo enumType = new(this, this.Reader, enumHandle);
        PrimitiveTypeCode declaredStorage = GetDeclaredEnumUnderlyingType(enumType);
        string name = this.Reader.GetString(this.Reader.GetTypeDefinition(enumHandle).Name);
        PrimitiveTypeCode? nativeStorage = null;
        string? firstSite = null;
        IEnumerable<Generator> generators = this.SuperGenerator is SuperGenerator super ? super.Generators.Values : [this];
        Generator[] consumers = generators.Where(consumer => consumer.GetAssociatedEnumReferenceClosure().Contains(this)).ToArray();
        if (consumers.Any(consumer => consumer.MetadataIndex.GetAssociatedEnumAttributes(name).Length > 0)
            && this.HasNativeEnumSignatures(enumHandle, consumers))
        {
            nativeStorage = declaredStorage;
        }
        else
        {
            foreach (Generator consumer in consumers)
            {
                foreach (CustomAttributeHandle handle in consumer.MetadataIndex.GetAssociatedEnumAttributes(name))
                {
                    CustomAttribute attribute = consumer.Reader.GetCustomAttribute(handle);
                    if (consumer.GetAssociatedEnumUseSiteAttributes(attribute) is not { } attributes)
                    {
                        continue;
                    }

                    HandleTypeHandleInfo target = consumer.ResolveAssociatedEnum(GetAssociatedEnumName(attribute), consumer.GetAssociatedEnumNamespace(attribute.Parent));
                    if (target.Generator != this || target.Handle != enumHandle)
                    {
                        continue;
                    }

                    consumer.FindAssociatedEnum(attributes);
                    (TypeHandleInfo Type, string Name) site = consumer.GetAssociatedEnumUseSite(attribute);
                    TypeHandleInfo element = site.Type;
                    while (element is ITypeHandleContainer container)
                    {
                        element = container.ElementType;
                    }

                    if (element is not PrimitiveTypeHandleInfo primitive || !IsEnumStorageType(primitive.PrimitiveTypeCode))
                    {
                        throw new GenerationFailedException($"AssociatedEnum \"{target}\" requires integral native storage at {site.Name}.");
                    }

                    PrimitiveTypeCode storage = primitive.PrimitiveTypeCode;
                    if (nativeStorage is PrimitiveTypeCode previous && previous != storage)
                    {
                        throw new GenerationFailedException($"AssociatedEnum \"{target}\" has conflicting native storage: {previous} at {firstSite}, and {storage} at {site.Name}. One projected enum cannot represent both widths/signedness.");
                    }

                    nativeStorage = storage;
                    firstSite = site.Name;
                }
            }
        }

        PrimitiveTypeCode result = nativeStorage ?? declaredStorage;
        TypeDefinition enumDefinition = this.Reader.GetTypeDefinition(enumHandle);
        string qualifiedName = $"{this.Reader.GetString(enumDefinition.Namespace)}.{name}";
        SpecialType expectedType = result switch
        {
            PrimitiveTypeCode.SByte => SpecialType.System_SByte, PrimitiveTypeCode.Byte => SpecialType.System_Byte,
            PrimitiveTypeCode.Int16 => SpecialType.System_Int16, PrimitiveTypeCode.UInt16 => SpecialType.System_UInt16,
            PrimitiveTypeCode.Int32 => SpecialType.System_Int32, PrimitiveTypeCode.UInt32 => SpecialType.System_UInt32,
            PrimitiveTypeCode.Int64 => SpecialType.System_Int64, PrimitiveTypeCode.UInt64 => SpecialType.System_UInt64,
            _ => throw new GenerationFailedException($"AssociatedEnum \"{qualifiedName}\" has unsupported native storage {result}."),
        };
        if (this.FindTypeSymbolIfAlreadyAvailable(qualifiedName) is INamedTypeSymbol { EnumUnderlyingType: { } underlying }
            && underlying.SpecialType != expectedType)
        {
            throw new GenerationFailedException($"AssociatedEnum \"{qualifiedName}\" requires {result} storage, but the referenced C# enum uses {underlying}.");
        }

        this.associatedEnumStorage.Add(enumHandle, result);
        return result;
    }

    private bool HasNativeEnumSignatures(TypeDefinitionHandle enumHandle, IEnumerable<Generator> consumers)
    {
        TypeDefinition definition = this.Reader.GetTypeDefinition(enumHandle);
        string name = this.Reader.GetString(definition.Name);
        string ns = this.Reader.GetString(definition.Namespace);
        foreach (Generator consumer in consumers)
        {
            foreach (EntityHandle handle in consumer.MetadataIndex.GetNativeSignatureTypes())
            {
                if (consumer == this && handle == enumHandle)
                {
                    return true;
                }

                if (handle.Kind == HandleKind.TypeReference)
                {
                    TypeReference reference = consumer.Reader.GetTypeReference((TypeReferenceHandle)handle);
                    if (consumer.Reader.StringComparer.Equals(reference.Name, name) && consumer.Reader.StringComparer.Equals(reference.Namespace, ns)
                        && consumer.TryGetTypeDefHandle(handle, out QualifiedTypeDefinitionHandle target)
                        && target.Generator == this && target.DefinitionHandle == enumHandle)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private CustomAttributeHandleCollection? GetAssociatedEnumUseSiteAttributes(CustomAttribute attribute)
    {
        if (attribute.Parent.Kind == HandleKind.FieldDefinition)
        {
            FieldDefinition field = this.Reader.GetFieldDefinition((FieldDefinitionHandle)attribute.Parent);
            TypeDefinition type = this.Reader.GetTypeDefinition(field.GetDeclaringType());
            CustomAttributeHandleCollection attributes = field.GetCustomAttributes();
            if (!this.IsCompatibleWithPlatform(type.GetCustomAttributes()) || !this.IsCompatibleWithPlatform(attributes))
            {
                return null;
            }

            return attributes;
        }

        ParameterHandle parameterHandle = (ParameterHandle)attribute.Parent;
        Parameter parameter = this.Reader.GetParameter(parameterHandle);
        MethodDefinition method = this.Reader.GetMethodDefinition(this.GetAssociatedEnumParameterOwner(parameterHandle));
        TypeDefinition declaringType = this.Reader.GetTypeDefinition(method.GetDeclaringType());
        return this.IsCompatibleWithPlatform(declaringType.GetCustomAttributes()) && this.IsCompatibleWithPlatform(method.GetCustomAttributes())
            && this.IsCompatibleWithPlatform(parameter.GetCustomAttributes()) ? parameter.GetCustomAttributes() : null;
    }

    private (TypeHandleInfo Type, string Name) GetAssociatedEnumUseSite(CustomAttribute attribute)
    {
        if (attribute.Parent.Kind == HandleKind.FieldDefinition)
        {
            FieldDefinition field = this.Reader.GetFieldDefinition((FieldDefinitionHandle)attribute.Parent);
            TypeDefinition type = this.Reader.GetTypeDefinition(field.GetDeclaringType());
            CustomAttributeHandleCollection attributes = field.GetCustomAttributes();
            TypeHandleInfo nativeType;
            if (this.FindAttribute(attributes, SystemRuntimeCompilerServices, nameof(FixedBufferAttribute)) is CustomAttribute fixedBuffer)
            {
                string elementName = fixedBuffer.DecodeValue(CustomAttributeTypeProvider.Instance).FixedArguments[0].Value!.ToString()!;
                PrimitiveTypeCode code = elementName switch
                {
                    "System.SByte" => PrimitiveTypeCode.SByte, "System.Byte" => PrimitiveTypeCode.Byte,
                    "System.Int16" => PrimitiveTypeCode.Int16, "System.UInt16" => PrimitiveTypeCode.UInt16,
                    "System.Int32" => PrimitiveTypeCode.Int32, "System.UInt32" => PrimitiveTypeCode.UInt32,
                    "System.Int64" => PrimitiveTypeCode.Int64, "System.UInt64" => PrimitiveTypeCode.UInt64,
                    _ => throw new GenerationFailedException($"AssociatedEnum cannot project fixed-buffer element type {elementName}."),
                };
                nativeType = new PrimitiveTypeHandleInfo(code);
            }
            else
            {
                nativeType = field.DecodeSignature(this.SignatureHandleProvider, null);
            }

            return (nativeType, $"{this.GetNamespaceForPossiblyNestedType(type)}.{this.Reader.GetString(type.Name)}.{this.Reader.GetString(field.Name)}");
        }

        ParameterHandle parameterHandle = (ParameterHandle)attribute.Parent;
        Parameter parameter = this.Reader.GetParameter(parameterHandle);
        MethodDefinition method = this.Reader.GetMethodDefinition(this.GetAssociatedEnumParameterOwner(parameterHandle));
        TypeDefinition declaringType = this.Reader.GetTypeDefinition(method.GetDeclaringType());
        MethodSignature<TypeHandleInfo> signature = method.DecodeSignature(this.SignatureHandleProvider, null);
        return (
            parameter.SequenceNumber == 0 ? signature.ReturnType : signature.ParameterTypes[parameter.SequenceNumber - 1],
            $"{this.GetNamespaceForPossiblyNestedType(declaringType)}.{this.Reader.GetString(declaringType.Name)}.{this.Reader.GetString(method.Name)}::{(parameter.SequenceNumber == 0 ? "return" : this.Reader.GetString(parameter.Name))}");
    }

    private MethodDeclarationSyntax? DeclareStoragePreservingEnumOverload(MethodDefinition method, MethodDeclarationSyntax nativeMethod, bool avoidWinmdRootAlias)
    {
        MethodSignature<TypeHandleInfo> signature = method.DecodeSignature(this.SignatureHandleProvider, null);
        ParameterListSyntax parameters = nativeMethod.ParameterList.WithParameters([.. nativeMethod.ParameterList.Parameters.Select(StripAttributes)]);
        ArgumentSyntax[] arguments = parameters.Parameters.Select(parameter => Argument(IdentifierName(parameter.Identifier))
            .WithRefKindKeyword(parameter.Modifiers.FirstOrDefault(modifier => modifier.Kind() is SyntaxKind.InKeyword or SyntaxKind.OutKeyword or SyntaxKind.RefKeyword))).ToArray();
        bool changed = false;
        TypeSyntaxSettings settings = this.extensionMethodSignatureTypeSettings with { AvoidWinmdRootAlias = avoidWinmdRootAlias };
        foreach (ParameterHandle handle in method.GetParameters())
        {
            Parameter parameter = this.Reader.GetParameter(handle);
            int index = parameter.SequenceNumber - 1;
            if (index < 0 || signature.ParameterTypes[index] is not PrimitiveTypeHandleInfo primitive
                || nativeMethod.ParameterList.Parameters[index].Type is not PredefinedTypeSyntax nativeType
                || this.FindAssociatedEnum(parameter.GetCustomAttributes()) is not HandleTypeHandleInfo enumType
                || !this.RequiresAssociatedEnumConversion(enumType, primitive.PrimitiveTypeCode))
            {
                continue;
            }

            TypeSyntax projected = GetAssociatedEnumTypeSyntax(enumType, settings, GeneratingElement.FriendlyOverload);
            parameters = parameters.ReplaceNode(parameters.Parameters[index], parameters.Parameters[index].WithType(projected.WithTrailingTrivia(Space)));
            arguments[index] = arguments[index].WithExpression(UncheckedExpression(CastExpression(nativeType.WithoutTrivia(), arguments[index].Expression)));
            changed = true;
        }

        TypeSyntax returnType = nativeMethod.ReturnType;
        ExpressionSyntax invocation = InvocationExpression(IdentifierName(nativeMethod.Identifier), [.. arguments]);
        if (signature.ReturnType is PrimitiveTypeHandleInfo returnPrimitive && returnType is PredefinedTypeSyntax
            && this.FindAssociatedEnum(this.GetReturnTypeCustomAttributes(method)) is HandleTypeHandleInfo returnEnum
            && this.RequiresAssociatedEnumConversion(returnEnum, returnPrimitive.PrimitiveTypeCode))
        {
            returnType = GetAssociatedEnumTypeSyntax(returnEnum, settings, GeneratingElement.FriendlyOverload);
            invocation = UncheckedExpression(CastExpression(returnType, invocation));
        }
        else if (!changed)
        {
            return null;
        }

        return nativeMethod.WithAttributeLists(default)
            .WithModifiers([.. nativeMethod.Modifiers.Where(modifier => !modifier.IsKind(SyntaxKind.ExternKeyword) && !modifier.IsKind(SyntaxKind.PartialKeyword))])
            .WithIdentifier(changed ? nativeMethod.Identifier : Identifier(nativeMethod.Identifier.ValueText + "_Enum"))
            .WithParameterList(parameters)
            .WithReturnType(returnType.WithTrailingTrivia(Space))
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
            declaringType = this.Reader.GetMethodDefinition(this.GetAssociatedEnumParameterOwner((ParameterHandle)owner)).GetDeclaringType();
        }
        else
        {
            throw new GenerationFailedException("AssociatedEnum is only supported on fields, parameters and return values.");
        }

        return this.GetNamespaceForPossiblyNestedType(this.Reader.GetTypeDefinition(declaringType));
    }

    private MethodDefinitionHandle GetAssociatedEnumParameterOwner(ParameterHandle parameter)
    {
        if (this.associatedEnumParameterOwners is null)
        {
            this.associatedEnumParameterOwners = new();
            foreach (MethodDefinitionHandle methodHandle in this.Reader.MethodDefinitions)
            {
                foreach (ParameterHandle handle in this.Reader.GetMethodDefinition(methodHandle).GetParameters())
                {
                    this.associatedEnumParameterOwners.Add(handle, methodHandle);
                }
            }
        }

        return this.associatedEnumParameterOwners[parameter];
    }
}
