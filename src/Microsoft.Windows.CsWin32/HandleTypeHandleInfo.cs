// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

internal record HandleTypeHandleInfo : TypeHandleInfo
{
    private readonly MetadataReader reader;
    private Generator? generator;

    // We just want to see that the identifier starts with I, followed by another upper case letter,
    // followed by a lower case letter. All the WinRT interfaces will match this, and none of the WinRT
    // objects will match it
    private static readonly System.Text.RegularExpressions.Regex InterfaceNameMatcher = new System.Text.RegularExpressions.Regex("^I[A-Z][a-z]");

    internal HandleTypeHandleInfo(MetadataReader reader, EntityHandle handle, byte? rawTypeKind = null)
    {
        this.reader = reader;
        this.Handle = handle;
        this.RawTypeKind = rawTypeKind;
        this.generator = null;
    }

    internal EntityHandle Handle { get; }

    internal byte? RawTypeKind { get; }

    public override string ToString() => this.ToTypeSyntaxForDisplay().ToString();

    internal override Generator? GetGenerator(Generator? inputGenerator)
    {
        if (this.generator is null)
        {
            inputGenerator?.SuperGenerator?.TryGetGenerator(this.reader, out this.generator);
        }

        return this.generator;
    }

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

    internal override TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, Generator.GeneratingElement forElement, QualifiedCustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes = default)
    {
        NameSyntax? nameSyntax;
        bool isInterface;
        bool isNonCOMConformingInterface;

        Generator typeGenerator = this.GetGenerator(inputs.Generator) ?? throw new ArgumentException("Generator required.");

        bool isManagedType = typeGenerator.IsManagedType(this);
        QualifiedTypeDefinitionHandle? qtdh = default;
        switch (this.Handle.Kind)
        {
            case HandleKind.TypeDefinition:
                TypeDefinition td = this.reader.GetTypeDefinition((TypeDefinitionHandle)this.Handle);
                bool hasUnmanagedSuffix = inputs.Generator?.HasUnmanagedSuffix(typeGenerator.Reader, td.Name, inputs.AllowMarshaling, isManagedType) ?? false;
                string simpleNameSuffix = hasUnmanagedSuffix ? Generator.UnmanagedInteropSuffix : string.Empty;
                nameSyntax = inputs.QualifyNames ? GetNestingQualifiedName(inputs.Generator, this.reader, td, hasUnmanagedSuffix, isInterfaceNestedInStruct: false) : IdentifierName(this.reader.GetString(td.Name) + simpleNameSuffix);
                isInterface = (td.Attributes & TypeAttributes.Interface) == TypeAttributes.Interface;
                isNonCOMConformingInterface = isInterface && typeGenerator.IsNonCOMInterface(td) is true;
                qtdh = new QualifiedTypeDefinitionHandle(typeGenerator, (TypeDefinitionHandle)this.Handle);
                break;
            case HandleKind.TypeReference:
                var trh = (TypeReferenceHandle)this.Handle;
                TypeReference tr = this.reader.GetTypeReference(trh);
                hasUnmanagedSuffix = inputs.Generator?.HasUnmanagedSuffix(typeGenerator.Reader, tr.Name, inputs.AllowMarshaling, isManagedType) ?? false;
                simpleNameSuffix = hasUnmanagedSuffix ? Generator.UnmanagedInteropSuffix : string.Empty;
                nameSyntax = inputs.QualifyNames ? GetNestingQualifiedName(inputs, this.reader, tr, hasUnmanagedSuffix) : IdentifierName(this.reader.GetString(tr.Name) + simpleNameSuffix);
                isInterface = typeGenerator.IsInterface(trh) is true;
                isNonCOMConformingInterface = isInterface && typeGenerator.IsNonCOMInterface(trh) is true;
                if (typeGenerator is not null)
                {
                    if (typeGenerator.TryGetTypeDefHandle(this.Handle, out QualifiedTypeDefinitionHandle qtdhTmp))
                    {
                        qtdh = qtdhTmp;
                    }
                }

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

        MarshalAsAttribute? marshalAs = null;
        bool isDelegate = this.IsDelegate(inputs, out QualifiedTypeDefinition delegateDefinition)
            && (qtdh is null || !Generator.IsUntypedDelegate(qtdh.Value.Reader, qtdh.Value.Reader.GetTypeDefinition(qtdh.Value.DefinitionHandle)));

        if (simpleName is "PWSTR" or "PSTR")
        {
            bool isConst = this.IsConstantField || MetadataUtilities.FindAttribute(customAttributes?.Reader!, customAttributes?.Collection, Generator.InteropDecorationNamespace, "ConstAttribute").HasValue;
            bool isEmptyStringTerminatedList = MetadataUtilities.FindAttribute(customAttributes?.Reader!, customAttributes?.Collection, Generator.InteropDecorationNamespace, "NullNullTerminatedAttribute").HasValue;
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
        else if (TryMarshalAsObject(inputs, simpleName, out marshalAs))
        {
            return new TypeSyntaxAndMarshaling(PredefinedType(Token(SyntaxKind.ObjectKeyword)), marshalAs, null);
        }
        else if (!inputs.AllowMarshaling && isDelegate && inputs.Generator is object && !Generator.IsUntypedDelegate(delegateDefinition.Generator.Reader, delegateDefinition.Definition))
        {
            return new TypeSyntaxAndMarshaling(inputs.Generator.FunctionPointer(delegateDefinition));
        }
        else
        {
            this.RequestTypeGeneration(typeGenerator, this.GetContext(inputs));
        }

        if (isDelegate)
        {
            marshalAs = new(UnmanagedType.FunctionPtr);
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

                    marshalAs = new MarshalAsAttribute(UnmanagedType.CustomMarshaler) { MarshalCookie = marshalCookie, MarshalType = Generator.WinRTCustomMarshalerFullName };
                }
            }
        }

        return new TypeSyntaxAndMarshaling(syntax, marshalAs, null);
    }

    internal override bool? IsValueType(TypeSyntaxSettings inputs)
    {
        Generator generator = this.GetGenerator(inputs.Generator) ?? throw new ArgumentException("Generator required.");
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

    private static NameSyntax GetNestingQualifiedName(TypeSyntaxSettings inputs, MetadataReader reader, TypeReferenceHandle handle, bool hasUnmanagedSuffix)
        => GetNestingQualifiedName(inputs, reader, reader.GetTypeReference(handle), hasUnmanagedSuffix);

    private static NameSyntax GetNestingQualifiedName(TypeSyntaxSettings inputs, MetadataReader reader, TypeReference tr, bool hasUnmanagedSuffix)
    {
        if (inputs.Generator is null)
        {
            throw new ArgumentException();
        }

        string simpleName = reader.GetString(tr.Name);
        if (hasUnmanagedSuffix)
        {
            simpleName += Generator.UnmanagedInteropSuffix;
        }

        SimpleNameSyntax typeName = IdentifierName(simpleName);
        return tr.ResolutionScope.Kind == HandleKind.TypeReference
            ? QualifiedName(GetNestingQualifiedName(inputs, reader, (TypeReferenceHandle)tr.ResolutionScope, hasUnmanagedSuffix), typeName)
            : QualifiedName(ParseName(inputs.AvoidWinmdRootAlias ? $"{Generator.GlobalNamespacePrefix}{reader.GetString(tr.Namespace)}" : Generator.ReplaceCommonNamespaceWithAlias(inputs.Generator, reader.GetString(tr.Namespace))), typeName);
    }

    private bool IsDelegate(TypeSyntaxSettings inputs, out QualifiedTypeDefinition delegateTypeDef)
    {
        TypeDefinitionHandle tdh = default;
        Generator? generator = this.GetGenerator(inputs.Generator);
        switch (this.Handle.Kind)
        {
            case HandleKind.TypeReference when generator is not null:
                var trHandle = (TypeReferenceHandle)this.Handle;
                if (generator.TryGetTypeDefHandle(trHandle, out QualifiedTypeDefinitionHandle qtdh))
                {
                    tdh = qtdh.DefinitionHandle;
                    generator = qtdh.Generator;
                }

                break;
            case HandleKind.TypeDefinition:
                tdh = (TypeDefinitionHandle)this.Handle;
                break;
        }

        if (!tdh.IsNil && generator is object)
        {
            TypeDefinition td = generator.Reader.GetTypeDefinition(tdh);
            if ((td.Attributes & TypeAttributes.Class) == TypeAttributes.Class)
            {
                generator.GetBaseTypeInfo(td, out StringHandle baseTypeName, out StringHandle baseTypeNamespace);
                if (!baseTypeName.IsNil)
                {
                    if (generator.Reader.StringComparer.Equals(baseTypeName, nameof(MulticastDelegate)) && generator.Reader.StringComparer.Equals(baseTypeNamespace, nameof(System)))
                    {
                        delegateTypeDef = new(generator, generator.Reader.GetTypeDefinition(tdh));
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
