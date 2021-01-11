// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System;
    using System.Collections.Generic;
    using System.Reflection.Metadata;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

    internal class CustomAttributeTypeProvider : ICustomAttributeTypeProvider<TypeSyntax>
    {
        private static readonly TypeSyntax IntPtrTypeSyntax = QualifiedName(IdentifierName("System"), IdentifierName(nameof(IntPtr)));
        private static readonly TypeSyntax UIntPtrTypeSyntax = QualifiedName(IdentifierName("System"), IdentifierName(nameof(UIntPtr)));

        public TypeSyntax GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return typeCode switch
            {
                PrimitiveTypeCode.Char => PredefinedType(Token(SyntaxKind.CharKeyword)),
                PrimitiveTypeCode.Boolean => PredefinedType(Token(SyntaxKind.BoolKeyword)),
                PrimitiveTypeCode.SByte => PredefinedType(Token(SyntaxKind.SByteKeyword)),
                PrimitiveTypeCode.Byte => PredefinedType(Token(SyntaxKind.ByteKeyword)),
                PrimitiveTypeCode.Int16 => PredefinedType(Token(SyntaxKind.ShortKeyword)),
                PrimitiveTypeCode.UInt16 => PredefinedType(Token(SyntaxKind.UShortKeyword)),
                PrimitiveTypeCode.Int32 => PredefinedType(Token(SyntaxKind.IntKeyword)),
                PrimitiveTypeCode.UInt32 => PredefinedType(Token(SyntaxKind.UIntKeyword)),
                PrimitiveTypeCode.Int64 => PredefinedType(Token(SyntaxKind.LongKeyword)),
                PrimitiveTypeCode.UInt64 => PredefinedType(Token(SyntaxKind.ULongKeyword)),
                PrimitiveTypeCode.Single => PredefinedType(Token(SyntaxKind.FloatKeyword)),
                PrimitiveTypeCode.Double => PredefinedType(Token(SyntaxKind.DoubleKeyword)),
                PrimitiveTypeCode.Object => PredefinedType(Token(SyntaxKind.ObjectKeyword)),
                PrimitiveTypeCode.String => PredefinedType(Token(SyntaxKind.StringKeyword)),
                PrimitiveTypeCode.IntPtr => IntPtrTypeSyntax,
                PrimitiveTypeCode.UIntPtr => UIntPtrTypeSyntax,
                PrimitiveTypeCode.Void => PredefinedType(Token(SyntaxKind.VoidKeyword)),
                _ => throw new NotSupportedException("Unsupported type code: " + typeCode),
            };
        }

        public TypeSyntax GetSystemType() => throw new NotImplementedException();

        public TypeSyntax GetSZArrayType(TypeSyntax elementType) => throw new NotImplementedException();

        public TypeSyntax GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => throw new NotImplementedException();

        public TypeSyntax GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var tr = reader.GetTypeReference(handle);
            string name = reader.GetString(tr.Name);
            string ns = reader.GetString(tr.Namespace);
            return ParseName(ns + "." + name);
        }

        public TypeSyntax GetTypeFromSerializedName(string name) => ParseName(name.IndexOf(',') is int index && index >= 0 ? name.Substring(0, index) : name);

        public PrimitiveTypeCode GetUnderlyingEnumType(TypeSyntax type) => PrimitiveTypeCode.Int32; // an assumption that works for now.

        public bool IsSystemType(TypeSyntax type) => type is QualifiedNameSyntax { Left: IdentifierNameSyntax { Identifier: { ValueText: "System" } }, Right: { Identifier: { ValueText: "Type" } } };
    }
}
