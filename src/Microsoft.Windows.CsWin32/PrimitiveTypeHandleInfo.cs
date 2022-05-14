// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.Windows.CsWin32.FastSyntaxFactory;

namespace Microsoft.Windows.CsWin32;

internal record PrimitiveTypeHandleInfo(PrimitiveTypeCode PrimitiveTypeCode) : TypeHandleInfo
{
    public override string ToString() => this.ToTypeSyntaxForDisplay().ToString();

    internal override TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, CustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes)
        => new TypeSyntaxAndMarshaling(ToTypeSyntax(this.PrimitiveTypeCode, inputs.PreferNativeInt));

    internal static TypeSyntax ToTypeSyntax(PrimitiveTypeCode typeCode, bool preferNativeInt)
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
            PrimitiveTypeCode.IntPtr => preferNativeInt ? IdentifierName("nint") : IdentifierName(nameof(IntPtr)),
            PrimitiveTypeCode.UIntPtr => preferNativeInt ? IdentifierName("nuint") : IdentifierName(nameof(UIntPtr)),
            PrimitiveTypeCode.Void => PredefinedType(Token(SyntaxKind.VoidKeyword)),
            _ => throw new NotSupportedException("Unsupported type code: " + typeCode),
        };
    }
}
