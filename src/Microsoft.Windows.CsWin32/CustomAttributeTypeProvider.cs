// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System;
    using System.Reflection.Metadata;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

    internal class CustomAttributeTypeProvider : ICustomAttributeTypeProvider<TypeSyntax>
    {
        internal static readonly CustomAttributeTypeProvider Instance = new CustomAttributeTypeProvider();

        private CustomAttributeTypeProvider()
        {
        }

        public TypeSyntax GetPrimitiveType(PrimitiveTypeCode typeCode) => PrimitiveTypeHandleInfo.ToTypeSyntax(typeCode, preferNativeInt: false);

        public TypeSyntax GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            // CONSIDER: reuse GetNestingQualifiedName (with namespace support added) here.
            var tr = reader.GetTypeReference(handle);
            string name = reader.GetString(tr.Name);
            string ns = reader.GetString(tr.Namespace);
            return ParseName(ns + "." + name);
        }

        public TypeSyntax GetTypeFromSerializedName(string name) => ParseName(name.IndexOf(',') is int index && index >= 0 ? name.Substring(0, index) : name);

        public PrimitiveTypeCode GetUnderlyingEnumType(TypeSyntax type) => PrimitiveTypeCode.Int32; // an assumption that works for now.

        public bool IsSystemType(TypeSyntax type) => type is QualifiedNameSyntax { Left: IdentifierNameSyntax { Identifier: { ValueText: "System" } }, Right: { Identifier: { ValueText: "Type" } } };

        public TypeSyntax GetSystemType() => throw new NotImplementedException();

        public TypeSyntax GetSZArrayType(TypeSyntax elementType) => throw new NotImplementedException();

        public TypeSyntax GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => throw new NotImplementedException();
    }
}
