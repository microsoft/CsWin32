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
    using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

    internal record HandleTypeHandleInfo : TypeHandleInfo
    {
        private readonly MetadataReader reader;

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
                    nameSyntax = inputs.QualifyNames ? GetNestingQualifiedName(this.reader, td) : IdentifierName(this.reader.GetString(td.Name));
                    isInterface = (td.Attributes & TypeAttributes.Interface) == TypeAttributes.Interface;
                    isNonCOMConformingInterface = isInterface && inputs.Generator?.IsNonCOMInterface(td) is true;
                    break;
                case HandleKind.TypeReference:
                    var trh = (TypeReferenceHandle)this.Handle;
                    TypeReference tr = this.reader.GetTypeReference(trh);
                    nameSyntax = inputs.QualifyNames ? GetNestingQualifiedName(this.reader, tr) : IdentifierName(this.reader.GetString(tr.Name));
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

            if (simpleName is "PWSTR" or "PSTR" && (this.IsConstantField || customAttributes?.Any(ah => Generator.IsAttribute(this.reader, this.reader.GetCustomAttribute(ah), Generator.InteropDecorationNamespace, "ConstAttribute")) is true))
            {
                IdentifierNameSyntax constantTypeIdentifierName = IdentifierName("PC" + simpleName.Substring(1));

                inputs.Generator?.RequestTypeDefStruct(constantTypeIdentifierName.Identifier.ValueText);
                return new TypeSyntaxAndMarshaling(constantTypeIdentifierName);
            }
            else if (TryMarshalAsObject(inputs, simpleName, out MarshalAsAttribute? marshalAs))
            {
                return new TypeSyntaxAndMarshaling(PredefinedType(Token(SyntaxKind.ObjectKeyword)).WithAdditionalAnnotations(Generator.IsManagedTypeAnnotation), marshalAs);
            }
            else if (!inputs.AllowMarshaling && this.IsDelegate(inputs, out TypeDefinition delegateDefinition) && inputs.Generator is object)
            {
                return new TypeSyntaxAndMarshaling(inputs.Generator.FunctionPointer(delegateDefinition));
            }
            else
            {
                this.RequestTypeGeneration(inputs.Generator);
            }

            TypeSyntax syntax = nameSyntax;

            if (isInterface is true)
            {
                syntax = inputs.AllowMarshaling && !isNonCOMConformingInterface ? syntax.WithAdditionalAnnotations(Generator.IsManagedTypeAnnotation) : PointerType(syntax);
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

        private static NameSyntax GetNestingQualifiedName(MetadataReader reader, TypeDefinitionHandle handle) => GetNestingQualifiedName(reader, reader.GetTypeDefinition(handle));

        private static NameSyntax GetNestingQualifiedName(MetadataReader reader, TypeDefinition td)
        {
            IdentifierNameSyntax name = IdentifierName(reader.GetString(td.Name));
            return td.GetDeclaringType() is { IsNil: false } nestingType
                ? QualifiedName(GetNestingQualifiedName(reader, nestingType), name)
                : name;
        }

        private static NameSyntax GetNestingQualifiedName(MetadataReader reader, TypeReferenceHandle handle) => GetNestingQualifiedName(reader, reader.GetTypeReference(handle));

        private static NameSyntax GetNestingQualifiedName(MetadataReader reader, TypeReference tr)
        {
            SimpleNameSyntax typeName = IdentifierName(reader.GetString(tr.Name));
            return tr.ResolutionScope.Kind == HandleKind.TypeReference
                ? QualifiedName(GetNestingQualifiedName(reader, (TypeReferenceHandle)tr.ResolutionScope), typeName)
                : tr.ResolutionScope.Kind == HandleKind.ModuleDefinition ? typeName : QualifiedName(ParseName(Generator.GlobalNamespacePrefix + reader.GetString(tr.Namespace)), typeName);
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
