// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    internal NativeArrayInfo? FindNativeArrayInfoAttribute(CustomAttributeHandleCollection customAttributeHandles)
    {
        return this.FindInteropDecorativeAttribute(customAttributeHandles, NativeArrayInfoAttribute) is CustomAttribute att
            ? DecodeNativeArrayInfoAttribute(att)
            : null;
    }

    internal CustomAttribute? FindInteropDecorativeAttribute(CustomAttributeHandleCollection? customAttributeHandles, string attributeName)
        => this.FindAttribute(customAttributeHandles, InteropDecorationNamespace, attributeName);

    internal CustomAttribute? FindAttribute(CustomAttributeHandleCollection? customAttributeHandles, string attributeNamespace, string attributeName)
        => MetadataUtilities.FindAttribute(this.Reader, customAttributeHandles, attributeNamespace, attributeName);

    internal IdentifierNameSyntax? FindAssociatedEnum(CustomAttributeHandleCollection? customAttributeHandles)
    {
        if (this.FindAttribute(customAttributeHandles, InteropDecorationNamespace, AssociatedEnumAttribute) is CustomAttribute att)
        {
            CustomAttributeValue<TypeSyntax> args = att.DecodeValue(CustomAttributeTypeProvider.Instance);
            return IdentifierName((string)args.FixedArguments[0].Value!);
        }

        return null;
    }

    internal bool TryGetTypeDefHandle(TypeReferenceHandle typeRefHandle, out QualifiedTypeDefinitionHandle typeDefHandle)
    {
        if (this.SuperGenerator is object)
        {
            return this.SuperGenerator.TryGetTypeDefinitionHandle(new QualifiedTypeReferenceHandle(this, typeRefHandle), out typeDefHandle);
        }

        if (this.MetadataIndex.TryGetTypeDefHandle(this.Reader, typeRefHandle, out TypeDefinitionHandle localTypeDefHandle))
        {
            typeDefHandle = new QualifiedTypeDefinitionHandle(this, localTypeDefHandle);
            return true;
        }

        typeDefHandle = default;
        return false;
    }

    internal bool TryGetTypeDefHandle(TypeReferenceHandle typeRefHandle, out TypeDefinitionHandle typeDefHandle) => this.MetadataIndex.TryGetTypeDefHandle(this.Reader, typeRefHandle, out typeDefHandle);

    internal bool TryGetTypeDefHandle(TypeReference typeRef, out TypeDefinitionHandle typeDefHandle) => this.TryGetTypeDefHandle(typeRef.Namespace, typeRef.Name, out typeDefHandle);

    internal bool TryGetTypeDefHandle(StringHandle @namespace, StringHandle name, out TypeDefinitionHandle typeDefHandle)
    {
        // PERF: Use an index
        foreach (TypeDefinitionHandle tdh in this.Reader.TypeDefinitions)
        {
            TypeDefinition td = this.Reader.GetTypeDefinition(tdh);
            if (td.Name.Equals(name) && td.Namespace.Equals(@namespace))
            {
                typeDefHandle = tdh;
                return true;
            }
        }

        typeDefHandle = default;
        return false;
    }

    internal bool TryGetTypeDefHandle(string @namespace, string name, out TypeDefinitionHandle typeDefinitionHandle)
    {
        // PERF: Use an index
        foreach (TypeDefinitionHandle tdh in this.Reader.TypeDefinitions)
        {
            TypeDefinition td = this.Reader.GetTypeDefinition(tdh);
            if (this.Reader.StringComparer.Equals(td.Name, name) && this.Reader.StringComparer.Equals(td.Namespace, @namespace))
            {
                typeDefinitionHandle = tdh;
                return true;
            }
        }

        typeDefinitionHandle = default;
        return false;
    }

    internal bool IsNonCOMInterface(TypeDefinition interfaceTypeDef)
    {
        if (this.Reader.StringComparer.Equals(interfaceTypeDef.Name, "IUnknown"))
        {
            return false;
        }

        // A conforming interface must have IUnknown as or an ancestor of its first base type.
        InterfaceImplementationHandle firstBaseInterface = interfaceTypeDef.GetInterfaceImplementations().FirstOrDefault();
        if (firstBaseInterface.IsNil)
        {
            return true;
        }

        InterfaceImplementation baseIFace = this.Reader.GetInterfaceImplementation(firstBaseInterface);
        TypeDefinitionHandle baseIFaceTypeDefHandle;
        if (baseIFace.Interface.Kind == HandleKind.TypeDefinition)
        {
            baseIFaceTypeDefHandle = (TypeDefinitionHandle)baseIFace.Interface;
        }
        else if (baseIFace.Interface.Kind == HandleKind.TypeReference)
        {
            if (!this.TryGetTypeDefHandle((TypeReferenceHandle)baseIFace.Interface, out baseIFaceTypeDefHandle))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        return this.IsNonCOMInterface(this.Reader.GetTypeDefinition(baseIFaceTypeDefHandle));
    }

    internal bool IsNonCOMInterface(TypeReferenceHandle interfaceTypeRefHandle) => this.TryGetTypeDefHandle(interfaceTypeRefHandle, out TypeDefinitionHandle tdh) && this.IsNonCOMInterface(this.Reader.GetTypeDefinition(tdh));

    internal bool IsInterface(HandleTypeHandleInfo typeInfo)
    {
        TypeDefinitionHandle tdh = default;
        if (typeInfo.Handle.Kind == HandleKind.TypeReference)
        {
            var trh = (TypeReferenceHandle)typeInfo.Handle;
            this.TryGetTypeDefHandle(trh, out tdh);
        }
        else if (typeInfo.Handle.Kind == HandleKind.TypeDefinition)
        {
            tdh = (TypeDefinitionHandle)typeInfo.Handle;
        }

        return !tdh.IsNil && (this.Reader.GetTypeDefinition(tdh).Attributes & TypeAttributes.Interface) == TypeAttributes.Interface;
    }

    internal bool IsInterface(TypeHandleInfo handleInfo)
    {
        if (handleInfo is HandleTypeHandleInfo typeInfo)
        {
            return this.IsInterface(typeInfo);
        }
        else if (handleInfo is PointerTypeHandleInfo { ElementType: HandleTypeHandleInfo typeInfo2 })
        {
            return this.IsInterface(typeInfo2);
        }

        return false;
    }

    internal bool IsInterface(TypeReferenceHandle typeRefHandle)
    {
        if (this.TryGetTypeDefHandle(typeRefHandle, out TypeDefinitionHandle typeDefHandle))
        {
            TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefHandle);
            return (typeDef.Attributes & TypeAttributes.Interface) == TypeAttributes.Interface;
        }

        return false;
    }

    internal bool IsDelegate(TypeDefinition typeDef) => (typeDef.Attributes & TypeAttributes.Class) == TypeAttributes.Class && typeDef.BaseType.Kind == HandleKind.TypeReference && this.Reader.StringComparer.Equals(this.Reader.GetTypeReference((TypeReferenceHandle)typeDef.BaseType).Name, nameof(MulticastDelegate));

    internal bool IsManagedType(TypeHandleInfo typeHandleInfo)
    {
        TypeHandleInfo elementType =
            typeHandleInfo is PointerTypeHandleInfo ptr ? ptr.ElementType :
            typeHandleInfo is ArrayTypeHandleInfo array ? array.ElementType :
            typeHandleInfo;
        if (elementType is PointerTypeHandleInfo ptr2)
        {
            return this.IsManagedType(ptr2.ElementType);
        }
        else if (elementType is PrimitiveTypeHandleInfo)
        {
            return false;
        }
        else if (elementType is HandleTypeHandleInfo { Handle: { Kind: HandleKind.TypeDefinition } typeDefHandle })
        {
            return this.IsManagedType((TypeDefinitionHandle)typeDefHandle);
        }
        else if (elementType is HandleTypeHandleInfo { Handle: { Kind: HandleKind.TypeReference } typeRefHandle } handleElement)
        {
            var trh = (TypeReferenceHandle)typeRefHandle;
            if (this.TryGetTypeDefHandle(trh, out TypeDefinitionHandle tdr))
            {
                return this.IsManagedType(tdr);
            }

            // If the type comes from an external assembly, assume that structs are blittable and anything else is not.
            TypeReference tr = this.Reader.GetTypeReference(trh);
            if (tr.ResolutionScope.Kind == HandleKind.AssemblyReference && handleElement.RawTypeKind is byte kind)
            {
                // Structs set 0x1, classes set 0x2.
                return (kind & 0x1) == 0;
            }
        }

        throw new GenerationFailedException("Unrecognized type: " + elementType.GetType().Name);
    }

    private static bool IsWideFunction(string methodName)
    {
        if (methodName.Length > 1 && methodName.EndsWith("W", StringComparison.Ordinal) && char.IsLower(methodName[methodName.Length - 2]))
        {
            // The name looks very much like an Wide-char method.
            // If further confidence is ever needed, we could look at the parameter and return types
            // to see if they have charset-related metadata in their marshaling metadata.
            return true;
        }

        return false;
    }

    private static bool IsAnsiFunction(string methodName)
    {
        if (methodName.Length > 1 && methodName.EndsWith("A", StringComparison.Ordinal) && char.IsLower(methodName[methodName.Length - 2]))
        {
            // The name looks very much like an Ansi method.
            // If further confidence is ever needed, we could look at the parameter and return types
            // to see if they have charset-related metadata in their marshaling metadata.
            return true;
        }

        return false;
    }

    private bool IsDelegateReference(TypeHandleInfo typeHandleInfo, out TypeDefinition delegateTypeDef)
    {
        if (typeHandleInfo is PointerTypeHandleInfo { ElementType: HandleTypeHandleInfo handleInfo })
        {
            return this.IsDelegateReference(handleInfo, out delegateTypeDef);
        }
        else if (typeHandleInfo is HandleTypeHandleInfo handleInfo1)
        {
            return this.IsDelegateReference(handleInfo1, out delegateTypeDef);
        }

        delegateTypeDef = default;
        return false;
    }

    private bool IsDelegateReference(HandleTypeHandleInfo typeHandleInfo, out TypeDefinition delegateTypeDef)
    {
        if (typeHandleInfo.Handle.Kind == HandleKind.TypeDefinition)
        {
            var tdh = (TypeDefinitionHandle)typeHandleInfo.Handle;
            delegateTypeDef = this.Reader.GetTypeDefinition(tdh);
            return this.IsDelegate(delegateTypeDef);
        }

        if (typeHandleInfo.Handle.Kind == HandleKind.TypeReference)
        {
            var trh = (TypeReferenceHandle)typeHandleInfo.Handle;
            if (this.TryGetTypeDefHandle(trh, out TypeDefinitionHandle tdh))
            {
                delegateTypeDef = this.Reader.GetTypeDefinition(tdh);
                return this.IsDelegate(delegateTypeDef);
            }
        }

        delegateTypeDef = default;
        return false;
    }

    private bool IsNestedType(EntityHandle typeHandle)
    {
        switch (typeHandle.Kind)
        {
            case HandleKind.TypeDefinition:
                TypeDefinition typeDef = this.Reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle);
                return typeDef.IsNested;
            case HandleKind.TypeReference:
                return this.TryGetTypeDefHandle((TypeReferenceHandle)typeHandle, out TypeDefinitionHandle typeDefHandle) && this.IsNestedType(typeDefHandle);
        }

        return false;
    }

    private bool IsManagedType(TypeDefinitionHandle typeDefinitionHandle)
    {
        if (this.managedTypesCheck.TryGetValue(typeDefinitionHandle, out bool result))
        {
            return result;
        }

        HashSet<TypeDefinitionHandle> visitedTypes = new();
        Dictionary<TypeDefinitionHandle, List<TypeDefinitionHandle>>? cycleFixups = null;
        result = Helper(typeDefinitionHandle)!.Value;

        // Dependency cycles may have prevented detection of managed types. Such may be managed if any in the cycle were ultimately deemed to be managed.
        if (cycleFixups?.Count > 0)
        {
            foreach (var fixup in cycleFixups)
            {
                if (this.managedTypesCheck[fixup.Key])
                {
                    foreach (TypeDefinitionHandle dependent in fixup.Value)
                    {
                        this.managedTypesCheck[dependent] = true;
                    }
                }
            }

            // This may have changed the result we are to return, so look up the current answer.
            result = this.managedTypesCheck[typeDefinitionHandle];
        }

        return result;

        bool? Helper(TypeDefinitionHandle typeDefinitionHandle)
        {
            if (this.managedTypesCheck.TryGetValue(typeDefinitionHandle, out bool result))
            {
                return result;
            }

            if (!visitedTypes.Add(typeDefinitionHandle))
            {
                // Avoid recursion. We just don't know the answer yet.
                return null;
            }

            TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefinitionHandle);
            try
            {
                if ((typeDef.Attributes & TypeAttributes.Interface) == TypeAttributes.Interface)
                {
                    result = this.options.AllowMarshaling && !this.IsNonCOMInterface(typeDef);
                    this.managedTypesCheck.Add(typeDefinitionHandle, result);
                    return result;
                }

                if ((typeDef.Attributes & TypeAttributes.Class) == TypeAttributes.Class && this.Reader.StringComparer.Equals(typeDef.Name, "Apis"))
                {
                    // We arguably should never be asked about this class, which is never generated.
                    this.managedTypesCheck.Add(typeDefinitionHandle, false);
                    return false;
                }

                this.GetBaseTypeInfo(typeDef, out StringHandle baseName, out StringHandle baseNamespace);
                if (this.Reader.StringComparer.Equals(baseName, nameof(ValueType)) && this.Reader.StringComparer.Equals(baseNamespace, nameof(System)))
                {
                    if (this.IsTypeDefStruct(typeDef))
                    {
                        this.managedTypesCheck.Add(typeDefinitionHandle, false);
                        return false;
                    }
                    else
                    {
                        foreach (FieldDefinitionHandle fieldHandle in typeDef.GetFields())
                        {
                            FieldDefinition fieldDef = this.Reader.GetFieldDefinition(fieldHandle);
                            try
                            {
                                TypeHandleInfo fieldType = fieldDef.DecodeSignature(SignatureHandleProvider.Instance, null);
                                TypeHandleInfo elementType = fieldType;
                                while (elementType is ITypeHandleContainer container)
                                {
                                    elementType = container.ElementType;
                                }

                                if (elementType is PrimitiveTypeHandleInfo)
                                {
                                    // These are never managed.
                                    continue;
                                }
                                else if (elementType is HandleTypeHandleInfo { Handle: { Kind: HandleKind.TypeDefinition } fieldTypeDefHandle })
                                {
                                    if (TestFieldAndHandleCycle((TypeDefinitionHandle)fieldTypeDefHandle) is true)
                                    {
                                        return true;
                                    }
                                }
                                else if (elementType is HandleTypeHandleInfo { Handle: { Kind: HandleKind.TypeReference } fieldTypeRefHandle })
                                {
                                    if (this.TryGetTypeDefHandle((TypeReferenceHandle)fieldTypeRefHandle, out TypeDefinitionHandle tdr) && TestFieldAndHandleCycle(tdr) is true)
                                    {
                                        return true;
                                    }
                                }
                                else
                                {
                                    throw new GenerationFailedException("Unrecognized type.");
                                }

                                bool? TestFieldAndHandleCycle(TypeDefinitionHandle tdh)
                                {
                                    bool? result = Helper(tdh);
                                    switch (result)
                                    {
                                        case true:
                                            this.managedTypesCheck.Add(typeDefinitionHandle, true);
                                            break;
                                        case null:
                                            cycleFixups ??= new();
                                            if (!cycleFixups.TryGetValue(tdh, out var list))
                                            {
                                                cycleFixups.Add(tdh, list = new());
                                            }

                                            list.Add(typeDefinitionHandle);
                                            break;
                                    }

                                    return result;
                                }
                            }
                            catch (Exception ex)
                            {
                                throw new GenerationFailedException($"Unable to ascertain whether the {this.Reader.GetString(fieldDef.Name)} field represents a managed type.", ex);
                            }
                        }

                        this.managedTypesCheck.Add(typeDefinitionHandle, false);
                        return false;
                    }
                }
                else if (this.Reader.StringComparer.Equals(baseName, nameof(Enum)) && this.Reader.StringComparer.Equals(baseNamespace, nameof(System)))
                {
                    this.managedTypesCheck.Add(typeDefinitionHandle, false);
                    return false;
                }
                else if (this.Reader.StringComparer.Equals(baseName, nameof(MulticastDelegate)) && this.Reader.StringComparer.Equals(baseNamespace, nameof(System)))
                {
                    // Delegates appear as unmanaged function pointers when using structs instead of COM interfaces.
                    // But certain delegates are never declared as delegates.
                    result = this.options.AllowMarshaling && !this.IsUntypedDelegate(typeDef);
                    this.managedTypesCheck.Add(typeDefinitionHandle, result);
                    return result;
                }

                throw new NotSupportedException();
            }
            catch (Exception ex)
            {
                throw new GenerationFailedException($"Unable to determine if {new HandleTypeHandleInfo(this.Reader, typeDefinitionHandle).ToTypeSyntax(this.errorMessageTypeSettings, null)} is a managed type.", ex);
            }
        }
    }

    private UnmanagedType? GetUnmanagedType(BlobHandle blobHandle)
    {
        if (blobHandle.IsNil)
        {
            return null;
        }

        BlobReader br = this.Reader.GetBlobReader(blobHandle);
        var unmgdType = (UnmanagedType)br.ReadByte();
        return unmgdType;
    }

    private bool IsCompilerGenerated(TypeDefinition typeDef) => this.FindAttribute(typeDef.GetCustomAttributes(), SystemRuntimeCompilerServices, nameof(CompilerGeneratedAttribute)).HasValue;

    private bool HasObsoleteAttribute(CustomAttributeHandleCollection attributes) => this.FindAttribute(attributes, nameof(System), nameof(ObsoleteAttribute)).HasValue;

    private CustomAttributeHandleCollection? GetReturnTypeCustomAttributes(MethodDefinition methodDefinition)
    {
        CustomAttributeHandleCollection? returnTypeAttributes = null;
        foreach (ParameterHandle parameterHandle in methodDefinition.GetParameters())
        {
            Parameter parameter = this.Reader.GetParameter(parameterHandle);
            if (parameter.Name.IsNil)
            {
                returnTypeAttributes = parameter.GetCustomAttributes();
            }

            // What we're looking for would always be the first element in the collection.
            break;
        }

        return returnTypeAttributes;
    }

    private bool IsUntypedDelegate(TypeDefinition typeDef) => IsUntypedDelegate(this.Reader, typeDef);

    private bool IsTypeDefStruct(TypeDefinition typeDef) => this.FindInteropDecorativeAttribute(typeDef.GetCustomAttributes(), NativeTypedefAttribute).HasValue || this.FindInteropDecorativeAttribute(typeDef.GetCustomAttributes(), MetadataTypedefAttribute).HasValue;

    private bool IsEmptyStructWithGuid(TypeDefinition typeDef)
    {
        return this.FindInteropDecorativeAttribute(typeDef.GetCustomAttributes(), nameof(GuidAttribute)).HasValue
            && typeDef.GetFields().Count == 0;
    }

    private AttributeSyntax? GetSupportedOSPlatformAttribute(CustomAttributeHandleCollection attributes)
    {
        AttributeSyntax? supportedOSPlatformAttribute = null;
        if (this.generateSupportedOSPlatformAttributes && this.FindInteropDecorativeAttribute(attributes, "SupportedOSPlatformAttribute") is CustomAttribute templateOSPlatformAttribute)
        {
            CustomAttributeValue<TypeSyntax> args = templateOSPlatformAttribute.DecodeValue(CustomAttributeTypeProvider.Instance);
            supportedOSPlatformAttribute = SupportedOSPlatformAttributeSyntax.AddArgumentListArguments(AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal((string)args.FixedArguments[0].Value!))));
        }

        return supportedOSPlatformAttribute;
    }

    /// <summary>
    /// Searches for an extern method.
    /// </summary>
    /// <param name="possiblyQualifiedName">A simple method name or one qualified with a namespace.</param>
    /// <param name="exactNameMatchOnly"><see langword="true"/> to only match on an exact method name; <see langword="false"/> to allow for fuzzy matching such as an omitted W or A suffix.</param>
    /// <returns>The matching method if exactly one is found, or <see langword="null"/> if none was found.</returns>
    /// <exception cref="ArgumentException">Thrown if the <paramref name="possiblyQualifiedName"/> argument is not qualified and more than one matching method name was found.</exception>
    private MethodDefinitionHandle? GetMethodByName(string possiblyQualifiedName, bool exactNameMatchOnly = false)
    {
        TrySplitPossiblyQualifiedName(possiblyQualifiedName, out string? methodNamespace, out string methodName);
        return this.GetMethodByName(methodNamespace, methodName, exactNameMatchOnly);
    }

    /// <summary>
    /// Searches for an extern method.
    /// </summary>
    /// <param name="methodNamespace">The namespace the method is found in, if known.</param>
    /// <param name="methodName">The simple name of the method.</param>
    /// <param name="exactNameMatchOnly"><see langword="true"/> to only match on an exact method name; <see langword="false"/> to allow for fuzzy matching such as an omitted W or A suffix.</param>
    /// <returns>The matching method if exactly one is found, or <see langword="null"/> if none was found.</returns>
    private MethodDefinitionHandle? GetMethodByName(string? methodNamespace, string methodName, bool exactNameMatchOnly = false)
    {
        IEnumerable<NamespaceMetadata> namespaces = this.GetNamespacesToSearch(methodNamespace);
        bool foundApiWithMismatchedPlatform = false;

        var matchingMethodHandles = new List<MethodDefinitionHandle>();
        foreach (NamespaceMetadata? nsMetadata in namespaces)
        {
            if (nsMetadata.Methods.TryGetValue(methodName, out MethodDefinitionHandle handle))
            {
                matchingMethodHandles.Add(handle);
            }
            else if (nsMetadata.MethodsForOtherPlatform.Contains(methodName))
            {
                foundApiWithMismatchedPlatform = true;
            }
        }

        if (!exactNameMatchOnly && matchingMethodHandles.Count == 0)
        {
            foreach (NamespaceMetadata? nsMetadata in namespaces)
            {
                if (nsMetadata.Methods.TryGetValue(methodName + "W", out MethodDefinitionHandle handle) ||
                    nsMetadata.Methods.TryGetValue(methodName + "A", out handle))
                {
                    matchingMethodHandles.Add(handle);
                }
            }
        }

        if (matchingMethodHandles.Count == 1)
        {
            return matchingMethodHandles[0];
        }
        else if (matchingMethodHandles.Count > 1)
        {
            string matches = string.Join(
                ", ",
                matchingMethodHandles.Select(h =>
                {
                    MethodDefinition md = this.Reader.GetMethodDefinition(h);
                    TypeDefinition td = this.Reader.GetTypeDefinition(md.GetDeclaringType());
                    return $"{this.Reader.GetString(td.Namespace)}.{this.Reader.GetString(md.Name)}";
                }));
            throw new ArgumentException("The method name is ambiguous. Use the fully-qualified name instead. Possible matches: " + matches);
        }

        if (foundApiWithMismatchedPlatform)
        {
            throw new PlatformIncompatibleException($"The requested API ({methodName}) was found but is not available given the target platform ({this.compilation?.Options.Platform}).");
        }

        return null;
    }
}
