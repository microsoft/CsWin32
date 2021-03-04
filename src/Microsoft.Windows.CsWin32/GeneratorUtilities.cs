// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal static class GeneratorUtilities
{
    internal static PredefinedTypeSyntax ToTypeOfConstant(MetadataReader mr, Constant constant)
    {
        var blobReader = mr.GetBlobReader(constant.Value);
        SyntaxKind keyword = constant.TypeCode switch
        {
            ConstantTypeCode.Boolean => SyntaxKind.BoolKeyword,
            ConstantTypeCode.Char => SyntaxKind.CharKeyword,
            ConstantTypeCode.SByte => SyntaxKind.SByteKeyword,
            ConstantTypeCode.Byte => SyntaxKind.ByteKeyword,
            ConstantTypeCode.Int16 => SyntaxKind.ShortKeyword,
            ConstantTypeCode.UInt16 => SyntaxKind.UShortKeyword,
            ConstantTypeCode.Int32 => SyntaxKind.IntKeyword,
            ConstantTypeCode.UInt32 => SyntaxKind.UIntKeyword,
            ConstantTypeCode.Int64 => SyntaxKind.LongKeyword,
            ConstantTypeCode.UInt64 => SyntaxKind.ULongKeyword,
            ConstantTypeCode.Single => SyntaxKind.FloatKeyword,
            ConstantTypeCode.Double => SyntaxKind.DoubleKeyword,
            ConstantTypeCode.String => SyntaxKind.StringKeyword,
            _ => throw new NotSupportedException("ConstantTypeCode not supported: " + constant.TypeCode),
        };
        return PredefinedType(Token(keyword));
    }

    internal static ExpressionSyntax ToExpressionSyntax(MetadataReader mr, Constant constant)
    {
        var blobReader = mr.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.Boolean => blobReader.ReadBoolean() ? LiteralExpression(SyntaxKind.TrueLiteralExpression) : LiteralExpression(SyntaxKind.FalseLiteralExpression),
            ConstantTypeCode.Char => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadChar())),
            ConstantTypeCode.SByte => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadSByte())),
            ConstantTypeCode.Byte => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadByte())),
            ConstantTypeCode.Int16 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadInt16())),
            ConstantTypeCode.UInt16 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadUInt16())),
            ConstantTypeCode.Int32 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadInt32())),
            ConstantTypeCode.UInt32 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadUInt32())),
            ConstantTypeCode.Int64 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadInt64())),
            ConstantTypeCode.UInt64 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadUInt64())),
            ConstantTypeCode.Single => FloatExpression(blobReader.ReadSingle()),
            ConstantTypeCode.Double => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadDouble())),
            ConstantTypeCode.String => blobReader.ReadConstant(constant.TypeCode) is string value ? LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value)) : LiteralExpression(SyntaxKind.NullLiteralExpression),
            ConstantTypeCode.NullReference => LiteralExpression(SyntaxKind.NullLiteralExpression),
            _ => throw new NotSupportedException("ConstantTypeCode not supported: " + constant.TypeCode),
        };

        static ExpressionSyntax FloatExpression(float value)
        {
            return
                float.IsPositiveInfinity(value) ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, PredefinedType(Token(SyntaxKind.FloatKeyword)), IdentifierName(nameof(float.PositiveInfinity))) :
                float.IsNegativeInfinity(value) ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, PredefinedType(Token(SyntaxKind.FloatKeyword)), IdentifierName(nameof(float.NegativeInfinity))) :
                float.IsNaN(value) ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, PredefinedType(Token(SyntaxKind.FloatKeyword)), IdentifierName(nameof(float.NaN))) :
                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(value));
        }
    }

    internal static ExpressionSyntax ToHexExpressionSyntax(MetadataReader mr, Constant constant)
    {
        var blobReader = mr.GetBlobReader(constant.Value);
        var blobReader2 = mr.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.SByte => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadSByte()), blobReader2.ReadSByte())),
            ConstantTypeCode.Byte => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadByte()), blobReader2.ReadByte())),
            ConstantTypeCode.Int16 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadInt16()), blobReader2.ReadInt16())),
            ConstantTypeCode.UInt16 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadUInt16()), blobReader2.ReadUInt16())),
            ConstantTypeCode.Int32 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadInt32()), blobReader2.ReadInt32())),
            ConstantTypeCode.UInt32 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadUInt32()), blobReader2.ReadUInt32())),
            ConstantTypeCode.Int64 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadInt64()), blobReader2.ReadInt64())),
            ConstantTypeCode.UInt64 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadUInt64()), blobReader2.ReadUInt64())),
            _ => throw new NotSupportedException("ConstantTypeCode not supported: " + constant.TypeCode),
        };

        unsafe string ToHex<T>(T value)
            where T : unmanaged
        {
            int fullHexLength = sizeof(T) * 2;
            string hex = string.Format(CultureInfo.InvariantCulture, "0x{0:X" + fullHexLength + "}", value);
            return hex;
        }
    }
}
