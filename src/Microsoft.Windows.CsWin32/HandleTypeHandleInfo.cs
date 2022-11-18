// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

internal record HandleTypeHandleInfo : TypeHandleInfo
{
    private readonly MetadataReader reader;

    // We just want to see that the identifier starts with I, followed by another upper case letter,
    // followed by a lower case letter. All the WinRT interfaces will match this, and none of the WinRT
    // objects will match it
    private static readonly System.Text.RegularExpressions.Regex InterfaceNameMatcher = new System.Text.RegularExpressions.Regex("^I[A-Z][a-z]");

    internal HandleTypeHandleInfo(MetadataReader reader, EntityHandle handle, byte? rawTypeKind = null)
    {
        this.reader = reader;
        this.Handle = handle;
        this.RawTypeKind = rawTypeKind;
    }

    internal EntityHandle Handle { get; }

    internal byte? RawTypeKind { get; }

    public override string ToString() => this.ToTypeSyntaxForDisplay().ToString();

    internal bool IsType(string leafName)
    {
        switch (this.Handle.Kind)
        {
            case HandleKind.TypeDefinition:
                TypeDefinition td = this.reader.GetTypeDefinition((TypeDefinitionHandle)this.Handle);
                return this.reader.StringComparer.Equals(td.Name, leafName);
            case HandleKind.TypeReference:
                var trh = (TypeReferenceHandle)this.Handle;
                TypeReference tr = this.reader.GetTypeReference(trh);
                return this.reader.StringComparer.Equals(tr.Name, leafName);
            default:
                throw new NotSupportedException("Unrecognized handle type.");
        }
    }

    internal override TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, CustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes = default)
    {
        NameSyntax? nameSyntax;
        bool isInterface;
        bool isNonCOMConformingInterface;
        bool isManagedType = inputs.Generator?.IsManagedType(this) ?? false;
        bool hasUnmanagedSuffix = inputs.Generator?.HasUnmanagedSuffix(inputs.AllowMarshaling, isManagedType) ?? false;
        string simpleNameSuffix = hasUnmanagedSuffix ? Generator.UnmanagedInteropSuffix : string.Empty;
        switch (this.Handle.Kind)
        {
            case HandleKind.TypeDefinition:
                TypeDefinition td = this.reader.GetTypeDefinition((TypeDefinitionHandle)this.Handle);
                nameSyntax = inputs.QualifyNames ? GetNestingQualifiedName(inputs.Generator, this.reader, td, hasUnmanagedSuffix, isInterfaceNestedInStruct: false) : IdentifierName(this.reader.GetString(td.Name) + simpleNameSuffix);
                isInterface = (td.Attributes & TypeAttributes.Interface) == TypeAttributes.Interface;
                isNonCOMConformingInterface = isInterface && inputs.Generator?.IsNonCOMInterface(td) is true;
                break;
            case HandleKind.TypeReference:
                var trh = (TypeReferenceHandle)this.Handle;
                TypeReference tr = this.reader.GetTypeReference(trh);
                nameSyntax = inputs.QualifyNames ? GetNestingQualifiedName(inputs.Generator, this.reader, tr, hasUnmanagedSuffix) : IdentifierName(this.reader.GetString(tr.Name) + simpleNameSuffix);
                isInterface = inputs.Generator?.IsInterface(trh) is true;
                isNonCOMConformingInterface = isInterface && inputs.Generator?.IsNonCOMInterface(trh) is true;
                break;
            default:
                throw new NotSupportedException("Unrecognized handle type.");
        }

        if (!TryGetSimpleName(nameSyntax, out string? simpleName))
        {
            throw new NotSupportedException("Unable to parse our own simple name.");
        }

        // Take this opportunity to ensure the type exists too.
        if (Generator.BclInteropStructs.TryGetValue(simpleName, out TypeSyntax? bclType))
        {
            return new TypeSyntaxAndMarshaling(bclType);
        }

        if (inputs.PreferMarshaledTypes && Generator.AdditionalBclInteropStructsMarshaled.TryGetValue(simpleName, out bclType))
        {
            return new TypeSyntaxAndMarshaling(bclType);
        }

        if (simpleName is "PWSTR" or "PSTR")
        {
            bool isConst = this.IsConstantField || MetadataUtilities.FindAttribute(this.reader, customAttributes, Generator.InteropDecorationNamespace, "ConstAttribute").HasValue;
            bool isEmptyStringTerminatedList = MetadataUtilities.FindAttribute(this.reader, customAttributes, Generator.InteropDecorationNamespace, "NullNullTerminatedAttribute").HasValue;
            string constChar = isConst ? "C" : string.Empty;
            string listChars = isEmptyStringTerminatedList ? "ZZ" : string.Empty;
            string nameEnding = simpleName.Substring(1);
            string specialName = $"P{constChar}{listChars}{nameEnding}";
            if (inputs.Generator is object)
            {
                if (Generator.SpecialTypeDefNames.Contains(specialName))
                {
                    inputs.Generator.RequestSpecialTypeDefStruct(specialName, out string fullyQualifiedName);
                    return new TypeSyntaxAndMarshaling(ParseName(Generator.ReplaceCommonNamespaceWithAlias(inputs.Generator, fullyQualifiedName)));
                }
                else
                {
                    this.RequestTypeGeneration(inputs.Generator, this.GetContext(inputs));
                }
            }
            else
            {
                return new TypeSyntaxAndMarshaling(IdentifierName(specialName));
            }
        }
        else if (TryMarshalAsObject(inputs, simpleName, out MarshalAsAttribute? marshalAs))
        {
            return new TypeSyntaxAndMarshaling(PredefinedType(Token(SyntaxKind.ObjectKeyword)), marshalAs, null);
        }
        else if (!inputs.AllowMarshaling && this.IsDelegate(inputs, out TypeDefinition delegateDefinition) && inputs.Generator is object && !Generator.IsUntypedDelegate(this.reader, delegateDefinition))
        {
            return new TypeSyntaxAndMarshaling(inputs.Generator.FunctionPointer(delegateDefinition));
        }
        else
        {
            this.RequestTypeGeneration(inputs.Generator, this.GetContext(inputs));
        }

        TypeSyntax syntax = isInterface && (!inputs.AllowMarshaling || isNonCOMConformingInterface)
            ? PointerType(nameSyntax)
            : nameSyntax;

        if (nameSyntax is QualifiedNameSyntax qualifiedName)
        {
            string? ns = qualifiedName.Left.ToString();

            // Look for WinRT namespaces
            if (ns.StartsWith("global::Windows.Foundation") || ns.StartsWith("global::Windows.UI") || ns.StartsWith("global::Windows.Graphics") || ns.StartsWith("global::Windows.System"))
            {
                // We only want to marshal WinRT objects, not interfaces. We don't have a good way of knowing
                // whether it's an interface or an object. "isInterface" comes back as false for a WinRT interface,
                // so that doesn't help. Looking at the name should be good enough, but if we needed to, the
                // Win32 projection could give us an attribute to make sure.
                string? objName = qualifiedName.Right.ToString();
                bool isInterfaceName = InterfaceNameMatcher.IsMatch(objName);
                if (!isInterfaceName)
                {
                    string marshalCookie = nameSyntax.ToString();
                    if (marshalCookie.StartsWith(Generator.GlobalNamespacePrefix, StringComparison.Ordinal))
                    {
                        marshalCookie = marshalCookie.Substring(Generator.GlobalNamespacePrefix.Length);
                    }

                    return new TypeSyntaxAndMarshaling(syntax, new MarshalAsAttribute(UnmanagedType.CustomMarshaler) { MarshalCookie = marshalCookie, MarshalType = Generator.WinRTCustomMarshalerFullName }, null);
                }
            }
        }

        return new TypeSyntaxAndMarshaling(syntax);
    }

    internal override bool? IsValueType(TypeSyntaxSettings inputs)
    {
        Generator generator = inputs.Generator ?? throw new ArgumentException("Generator required.");
        TypeDefinitionHandle typeDefHandle = default;
        switch (this.Handle.Kind)
        {
            case HandleKind.TypeDefinition:
                typeDefHandle = (TypeDefinitionHandle)this.Handle;
                break;
            case HandleKind.TypeReference:
                if (generator.TryGetTypeDefHandle((TypeReferenceHandle)this.Handle, out QualifiedTypeDefinitionHandle qualifiedTypeDefHandle))
                {
                    generator = qualifiedTypeDefHandle.Generator;
                    typeDefHandle = qualifiedTypeDefHandle.DefinitionHandle;
                }

                break;
            default:
                return null;
        }

        if (typeDefHandle.IsNil)
        {
            return null;
        }

        TypeDefinition typeDef = generator.Reader.GetTypeDefinition(typeDefHandle);
        generator.GetBaseTypeInfo(typeDef, out StringHandle baseName, out StringHandle baseNamespace);
        if (generator.Reader.StringComparer.Equals(baseName, nameof(ValueType)) && generator.Reader.StringComparer.Equals(baseNamespace, nameof(System)))
        {
            // When marshaling, the VARIANT struct becomes object, which is *not* a value type.
            if (inputs.AllowMarshaling && generator.Reader.StringComparer.Equals(typeDef.Name, "VARIANT"))
            {
                return false;
            }

            return true;
        }

        if (generator.Reader.StringComparer.Equals(baseName, nameof(Enum)) && generator.Reader.StringComparer.Equals(baseNamespace, nameof(System)))
        {
            return true;
        }

        return false;
    }

    private static bool TryMarshalAsObject(TypeSyntaxSettings inputs, string name, [NotNullWhen(true)] out MarshalAsAttribute? marshalAs)
    {
        if (inputs.AllowMarshaling)
        {
            switch (name)
            {
                case "IUnknown":
                    marshalAs = new MarshalAsAttribute(UnmanagedType.IUnknown);
                    return true;
                case "IDispatch":
                    marshalAs = new MarshalAsAttribute(UnmanagedType.IDispatch);
                    return true;
                case "VARIANT":
                    marshalAs = new MarshalAsAttribute(UnmanagedType.Struct);
                    return true;
            }
        }

        marshalAs = null;
        return false;
    }

    private static NameSyntax GetNestingQualifiedName(Generator? generator, MetadataReader reader, TypeDefinitionHandle handle, bool hasUnmanagedSuffix, bool isInterfaceNestedInStruct) => GetNestingQualifiedName(generator, reader, reader.GetTypeDefinition(handle), hasUnmanagedSuffix, isInterfaceNestedInStruct);

    internal static NameSyntax GetNestingQualifiedName(Generator? generator, MetadataReader reader, TypeDefinition td, bool hasUnmanagedSuffix, bool isInterfaceNestedInStruct)
    {
        string simpleName = reader.GetString(td.Name);
        if (hasUnmanagedSuffix)
        {
            simpleName += Generator.UnmanagedInteropSuffix;
        }

        IdentifierNameSyntax name = IdentifierName(simpleName);
        if (td.GetDeclaringType() is { IsNil: false } nestingType)
        {
            // This type is nested in another, so it is qualified by the parent type rather than by its own namespace.
            return QualifiedName(GetNestingQualifiedName(generator, reader, nestingType, hasUnmanagedSuffix, isInterfaceNestedInStruct), name);
        }
        else
        {
            // This is not a nested type.
            NameSyntax result = QualifiedName(ParseName(Generator.ReplaceCommonNamespaceWithAlias(generator, reader.GetString(td.Namespace))), name);

            if (isInterfaceNestedInStruct)
            {
                // Such an interface has a special name, under the original one.
                result = QualifiedName(result, Generator.NestedCOMInterfaceName);
            }

            return result;
        }
    }

    private static NameSyntax GetNestingQualifiedName(Generator? generator, MetadataReader reader, TypeReferenceHandle handle, bool hasUnmanagedSuffix) => GetNestingQualifiedName(generator, reader, reader.GetTypeReference(handle), hasUnmanagedSuffix);

    private static NameSyntax GetNestingQualifiedName(Generator? generator, MetadataReader reader, TypeReference tr, bool hasUnmanagedSuffix)
    {
        string simpleName = reader.GetString(tr.Name);
        if (hasUnmanagedSuffix)
        {
            simpleName += Generator.UnmanagedInteropSuffix;
        }

        SimpleNameSyntax typeName = IdentifierName(simpleName);
        return tr.ResolutionScope.Kind == HandleKind.TypeReference
            ? QualifiedName(GetNestingQualifiedName(generator, reader, (TypeReferenceHandle)tr.ResolutionScope, hasUnmanagedSuffix), typeName)
            : QualifiedName(ParseName(Generator.ReplaceCommonNamespaceWithAlias(generator, reader.GetString(tr.Namespace))), typeName);
    }

    private bool IsDelegate(TypeSyntaxSettings inputs, out TypeDefinition delegateTypeDef)
    {
        TypeDefinitionHandle tdh = default;
        switch (this.Handle.Kind)
        {
            case HandleKind.TypeReference:
                var trHandle = (TypeReferenceHandle)this.Handle;
                inputs.Generator?.TryGetTypeDefHandle(trHandle, out tdh);
                break;
            case HandleKind.TypeDefinition:
                tdh = (TypeDefinitionHandle)this.Handle;
                break;
        }

        if (!tdh.IsNil && inputs.Generator is object)
        {
            TypeDefinition td = this.reader.GetTypeDefinition(tdh);
            if ((td.Attributes & TypeAttributes.Class) == TypeAttributes.Class)
            {
                inputs.Generator.GetBaseTypeInfo(td, out StringHandle baseTypeName, out StringHandle baseTypeNamespace);
                if (!baseTypeName.IsNil)
                {
                    if (this.reader.StringComparer.Equals(baseTypeName, nameof(MulticastDelegate)) && this.reader.StringComparer.Equals(baseTypeNamespace, nameof(System)))
                    {
                        delegateTypeDef = this.reader.GetTypeDefinition(tdh);
                        return true;
                    }
                }
            }
        }

        delegateTypeDef = default;
        return false;
    }

    private void RequestTypeGeneration(Generator? generator, Generator.Context context)
    {
        if (this.Handle.Kind == HandleKind.TypeDefinition)
        {
            generator?.RequestInteropType((TypeDefinitionHandle)this.Handle, context);
        }
        else if (this.Handle.Kind == HandleKind.TypeReference)
        {
            generator?.RequestInteropType((TypeReferenceHandle)this.Handle, context);
        }
    }
}
