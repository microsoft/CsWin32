// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    private readonly Dictionary<EntityHandle, HandleTypeHandleInfo?> associatedEnums = new();
    private readonly List<Diagnostic> generationDiagnostics = new();
    private Dictionary<ParameterHandle, MethodDefinitionHandle>? associatedEnumParameterOwners;

    /// <summary>
    /// Gets diagnostics produced while projecting metadata.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics => this.generationDiagnostics;

    /// <summary>
    /// Resolves an enum association whose storage matches the annotated native value.
    /// </summary>
    /// <param name="customAttributeHandles">The attributes on the native value.</param>
    /// <returns>The associated enum, or null if absent or incompatible with the native representation.</returns>
    internal HandleTypeHandleInfo? FindAssociatedEnum(CustomAttributeHandleCollection? customAttributeHandles)
    {
        if (this.FindAttribute(customAttributeHandles, InteropDecorationNamespace, AssociatedEnumAttribute) is not CustomAttribute attribute)
        {
            return null;
        }

        if (this.associatedEnums.TryGetValue(attribute.Parent, out HandleTypeHandleInfo? cached))
        {
            return cached;
        }

        CustomAttributeValue<TypeSyntax> arguments = attribute.DecodeValue(CustomAttributeTypeProvider.Instance);
        if (arguments.FixedArguments is not [{ Value: string name }] || string.IsNullOrWhiteSpace(name))
        {
            throw new GenerationFailedException("AssociatedEnum requires a nonempty enum name.");
        }

        (TypeHandleInfo nativeType, string scope, string site) = this.GetAssociatedEnumUseSite(attribute.Parent);
        HandleTypeHandleInfo enumType = this.ResolveAssociatedEnum(name, scope);
        PrimitiveTypeCode enumStorage = GetAssociatedEnumStorage(enumType);
        while (nativeType is ITypeHandleContainer container)
        {
            nativeType = container.ElementType;
        }

        string? incompatibility = null;
        if (this.FindAttribute(customAttributeHandles, InteropDecorationNamespace, NativeBitfieldAttribute) is not null)
        {
            incompatibility = "the annotated field is also a NativeBitfield backing field";
        }
        else if (nativeType is PrimitiveTypeHandleInfo primitive)
        {
            if (primitive.PrimitiveTypeCode != enumStorage)
            {
                incompatibility = $"native storage is {primitive.PrimitiveTypeCode}, but the enum's underlying type is {enumStorage}";
            }
        }
        else if (nativeType is not HandleTypeHandleInfo nativeHandle
            || !nativeHandle.Generator.TryGetTypeDefHandle(nativeHandle.Handle, out QualifiedTypeDefinitionHandle definition)
            || definition.Generator != enumType.Generator || definition.DefinitionHandle != enumType.Handle)
        {
            incompatibility = $"native type {nativeType} is not the enum's underlying integral type";
        }

        TypeDefinition enumDefinition = enumType.Reader.GetTypeDefinition((TypeDefinitionHandle)enumType.Handle);
        string qualifiedName = $"{enumType.Reader.GetString(enumDefinition.Namespace)}.{enumType.Reader.GetString(enumDefinition.Name)}";
        if (incompatibility is null && enumType.Generator.FindTypeSymbolIfAlreadyAvailable(qualifiedName) is INamedTypeSymbol availableType
            && availableType.EnumUnderlyingType?.SpecialType != GetEnumSpecialType(enumStorage))
        {
            incompatibility = $"the available C# type {qualifiedName} does not have the enum's {enumStorage} storage";
        }

        if (incompatibility is not null)
        {
            this.generationDiagnostics.Add(Diagnostic.Create(SourceGenerator.IncompatibleAssociatedEnum, Location.None, name, site, incompatibility));
            this.associatedEnums.Add(attribute.Parent, null);
            return null;
        }

        this.associatedEnums.Add(attribute.Parent, enumType);
        return enumType;
    }

    private static PrimitiveTypeCode GetAssociatedEnumStorage(HandleTypeHandleInfo enumType)
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

    private static SpecialType GetEnumSpecialType(PrimitiveTypeCode storage) => storage switch
    {
        PrimitiveTypeCode.SByte => SpecialType.System_SByte,
        PrimitiveTypeCode.Byte => SpecialType.System_Byte,
        PrimitiveTypeCode.Int16 => SpecialType.System_Int16,
        PrimitiveTypeCode.UInt16 => SpecialType.System_UInt16,
        PrimitiveTypeCode.Int32 => SpecialType.System_Int32,
        PrimitiveTypeCode.UInt32 => SpecialType.System_UInt32,
        PrimitiveTypeCode.Int64 => SpecialType.System_Int64,
        PrimitiveTypeCode.UInt64 => SpecialType.System_UInt64,
        _ => throw new GenerationFailedException($"Unsupported enum storage {storage}."),
    };

    private static PrimitiveTypeCode GetFixedBufferElementType(CustomAttribute fixedBuffer)
    {
        string elementName = fixedBuffer.DecodeValue(CustomAttributeTypeProvider.Instance).FixedArguments[0].Value!.ToString()!;
        return elementName switch
        {
            "System.SByte" => PrimitiveTypeCode.SByte,
            "System.Byte" => PrimitiveTypeCode.Byte,
            "System.Int16" => PrimitiveTypeCode.Int16,
            "System.UInt16" => PrimitiveTypeCode.UInt16,
            "System.Int32" => PrimitiveTypeCode.Int32,
            "System.UInt32" => PrimitiveTypeCode.UInt32,
            "System.Int64" => PrimitiveTypeCode.Int64,
            "System.UInt64" => PrimitiveTypeCode.UInt64,
            "System.Char" => PrimitiveTypeCode.Char,
            "System.Single" => PrimitiveTypeCode.Single,
            "System.Double" => PrimitiveTypeCode.Double,
            "System.Boolean" => PrimitiveTypeCode.Boolean,
            _ => throw new GenerationFailedException($"Unsupported fixed-buffer element type {elementName}."),
        };
    }

    private HandleTypeHandleInfo ResolveAssociatedEnum(string name, string scope)
    {
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

        return matches[0];
    }

    private (TypeHandleInfo Type, string Namespace, string Site) GetAssociatedEnumUseSite(EntityHandle owner)
    {
        if (owner.Kind == HandleKind.FieldDefinition)
        {
            FieldDefinition field = this.Reader.GetFieldDefinition((FieldDefinitionHandle)owner);
            TypeDefinition declaringType = this.Reader.GetTypeDefinition(field.GetDeclaringType());
            string scope = this.GetNamespaceForPossiblyNestedType(declaringType);
            TypeHandleInfo nativeType = field.DecodeSignature(this.SignatureHandleProvider, null);
            if (this.FindAttribute(field.GetCustomAttributes(), SystemRuntimeCompilerServices, nameof(FixedBufferAttribute)) is CustomAttribute fixedBuffer)
            {
                nativeType = new PrimitiveTypeHandleInfo(GetFixedBufferElementType(fixedBuffer));
            }

            return (nativeType, scope, $"{scope}.{this.Reader.GetString(declaringType.Name)}.{this.Reader.GetString(field.Name)}");
        }

        if (owner.Kind != HandleKind.Parameter)
        {
            throw new GenerationFailedException("AssociatedEnum is only supported on fields, parameters and return values.");
        }

        ParameterHandle parameterHandle = (ParameterHandle)owner;
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

        Parameter parameter = this.Reader.GetParameter(parameterHandle);
        MethodDefinition method = this.Reader.GetMethodDefinition(this.associatedEnumParameterOwners[parameterHandle]);
        TypeDefinition type = this.Reader.GetTypeDefinition(method.GetDeclaringType());
        MethodSignature<TypeHandleInfo> signature = method.DecodeSignature(this.SignatureHandleProvider, null);
        string ns = this.GetNamespaceForPossiblyNestedType(type);
        return (
            parameter.SequenceNumber == 0 ? signature.ReturnType : signature.ParameterTypes[parameter.SequenceNumber - 1],
            ns,
            $"{ns}.{this.Reader.GetString(type.Name)}.{this.Reader.GetString(method.Name)}::{(parameter.SequenceNumber == 0 ? "return" : this.Reader.GetString(parameter.Name))}");
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
}
