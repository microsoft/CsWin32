// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Metadata;
    using System.Runtime.InteropServices;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using static FastSyntaxFactory;

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

        internal override TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, CustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes = default)
        {
            NameSyntax? nameSyntax;
            bool isInterface;
            bool isNonCOMConformingInterface;
            switch (this.Handle.Kind)
            {
                case HandleKind.TypeDefinition:
                    TypeDefinition td = this.reader.GetTypeDefinition((TypeDefinitionHandle)this.Handle);
                    nameSyntax = inputs.QualifyNames ? GetNestingQualifiedName(inputs.Generator, this.reader, td) : IdentifierName(this.reader.GetString(td.Name));
                    isInterface = (td.Attributes & TypeAttributes.Interface) == TypeAttributes.Interface;
                    isNonCOMConformingInterface = isInterface && inputs.Generator?.IsNonCOMInterface(td) is true;
                    break;
                case HandleKind.TypeReference:
                    var trh = (TypeReferenceHandle)this.Handle;
                    TypeReference tr = this.reader.GetTypeReference(trh);
                    nameSyntax = inputs.QualifyNames ? GetNestingQualifiedName(inputs.Generator, this.reader, tr) : IdentifierName(this.reader.GetString(tr.Name));
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

            if (simpleName is "PWSTR" or "PSTR" && (this.IsConstantField || customAttributes?.Any(ah => MetadataUtilities.IsAttribute(this.reader, this.reader.GetCustomAttribute(ah), Generator.InteropDecorationNamespace, "ConstAttribute")) is true))
            {
                string specialName = "PC" + simpleName.Substring(1);
                if (inputs.Generator is object)
                {
                    inputs.Generator.RequestSpecialTypeDefStruct(specialName, out string fullyQualifiedName);
                    return new TypeSyntaxAndMarshaling(ParseName(Generator.ReplaceCommonNamespaceWithAlias(inputs.Generator, fullyQualifiedName)));
                }
                else
                {
                    return new TypeSyntaxAndMarshaling(IdentifierName(specialName));
                }
            }
            else if (TryMarshalAsObject(inputs, simpleName, out MarshalAsAttribute? marshalAs))
            {
                return new TypeSyntaxAndMarshaling(PredefinedType(Token(SyntaxKind.ObjectKeyword)), marshalAs);
            }
            else if (!inputs.AllowMarshaling && this.IsDelegate(inputs, out TypeDefinition delegateDefinition) && inputs.Generator is object && !Generator.IsUntypedDelegate(this.reader, delegateDefinition))
            {
                return new TypeSyntaxAndMarshaling(inputs.Generator.FunctionPointer(delegateDefinition));
            }
            else
            {
                this.RequestTypeGeneration(inputs.Generator);
            }

            TypeSyntax syntax = isInterface && (!inputs.AllowMarshaling || isNonCOMConformingInterface)
                ? PointerType(nameSyntax)
                : nameSyntax;

            if (nameSyntax is QualifiedNameSyntax qualifiedName)
            {
                var ns = qualifiedName.Left.ToString();

                // Look for WinRT namespaces
                if (ns.StartsWith("global::Windows.Foundation") || ns.StartsWith("global::Windows.UI") || ns.StartsWith("global::Windows.Graphics") || ns.StartsWith("global::Windows.System"))
                {
                    // We only want to marshal WinRT objects, not interfaces. We don't have a good way of knowing
                    // whether it's an interface or an object. "isInterface" comes back as false for a WinRT interface,
                    // so that doesn't help. Looking at the name should be good enough, but if we needed to, the
                    // Win32 projection could give us an attribute to make sure
                    var objName = qualifiedName.Right.ToString();
                    bool isInterfaceName = InterfaceNameMatcher.IsMatch(objName);
                    if (!isInterfaceName)
                    {
                        string marshalCookie = nameSyntax.ToString();
                        if (marshalCookie.StartsWith(Generator.GlobalNamespacePrefix, StringComparison.Ordinal))
                        {
                            marshalCookie = marshalCookie.Substring(Generator.GlobalNamespacePrefix.Length);
                        }

                        return new TypeSyntaxAndMarshaling(syntax, new MarshalAsAttribute(UnmanagedType.CustomMarshaler) { MarshalCookie = marshalCookie, MarshalType = Generator.WinRTCustomMarshalerFullName });
                    }
                }
            }

            return new TypeSyntaxAndMarshaling(syntax);
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

        private static NameSyntax GetNestingQualifiedName(Generator? generator, MetadataReader reader, TypeDefinitionHandle handle) => GetNestingQualifiedName(generator, reader, reader.GetTypeDefinition(handle));

        internal static NameSyntax GetNestingQualifiedName(Generator? generator, MetadataReader reader, TypeDefinition td)
        {
            IdentifierNameSyntax name = IdentifierName(reader.GetString(td.Name));
            return td.GetDeclaringType() is { IsNil: false } nestingType
                ? QualifiedName(GetNestingQualifiedName(generator, reader, nestingType), name)
                : QualifiedName(ParseName(Generator.ReplaceCommonNamespaceWithAlias(generator, reader.GetString(td.Namespace))), name);
        }

        private static NameSyntax GetNestingQualifiedName(Generator? generator, MetadataReader reader, TypeReferenceHandle handle) => GetNestingQualifiedName(generator, reader, reader.GetTypeReference(handle));

        private static NameSyntax GetNestingQualifiedName(Generator? generator, MetadataReader reader, TypeReference tr)
        {
            SimpleNameSyntax typeName = IdentifierName(reader.GetString(tr.Name));
            return tr.ResolutionScope.Kind == HandleKind.TypeReference
                ? QualifiedName(GetNestingQualifiedName(generator, reader, (TypeReferenceHandle)tr.ResolutionScope), typeName)
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

        private void RequestTypeGeneration(Generator? generator)
        {
            if (this.Handle.Kind == HandleKind.TypeDefinition)
            {
                generator?.RequestInteropType((TypeDefinitionHandle)this.Handle);
            }
            else if (this.Handle.Kind == HandleKind.TypeReference)
            {
                generator?.RequestInteropType((TypeReferenceHandle)this.Handle);
            }
        }
    }
}
