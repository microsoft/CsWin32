// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

internal record PrimitiveTypeHandleInfo(PrimitiveTypeCode PrimitiveTypeCode) : TypeHandleInfo
{
    public override string ToString() => this.ToTypeSyntaxForDisplay().ToString();

    internal override TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, CustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes)
        => new TypeSyntaxAndMarshaling(ToTypeSyntax(this.PrimitiveTypeCode, inputs.PreferNativeInt));

    internal override bool? IsValueType(TypeSyntaxSettings inputs)
    {
        return this.PrimitiveTypeCode is not PrimitiveTypeCode.Object or PrimitiveTypeCode.Void;
    }

    internal static TypeSyntax ToTypeSyntax(PrimitiveTypeCode typeCode, bool preferNativeInt)
    {
        return typeCode switch
        {
            PrimitiveTypeCode.Char => SyntaxRecycleBin.Common.PredefinedType.Char,
            PrimitiveTypeCode.Boolean => SyntaxRecycleBin.Common.PredefinedType.Boolean,
            PrimitiveTypeCode.SByte => SyntaxRecycleBin.Common.PredefinedType.SByte,
            PrimitiveTypeCode.Byte => SyntaxRecycleBin.Common.PredefinedType.Byte,
            PrimitiveTypeCode.Int16 => SyntaxRecycleBin.Common.PredefinedType.Int16,
            PrimitiveTypeCode.UInt16 => SyntaxRecycleBin.Common.PredefinedType.UInt16,
            PrimitiveTypeCode.Int32 => SyntaxRecycleBin.Common.PredefinedType.Int32,
            PrimitiveTypeCode.UInt32 => SyntaxRecycleBin.Common.PredefinedType.UInt32,
            PrimitiveTypeCode.Int64 => SyntaxRecycleBin.Common.PredefinedType.Int64,
            PrimitiveTypeCode.UInt64 => SyntaxRecycleBin.Common.PredefinedType.UInt64,
            PrimitiveTypeCode.Single => SyntaxRecycleBin.Common.PredefinedType.Single,
            PrimitiveTypeCode.Double => SyntaxRecycleBin.Common.PredefinedType.Double,
            PrimitiveTypeCode.Object => SyntaxRecycleBin.Common.PredefinedType.Object,
            PrimitiveTypeCode.String => SyntaxRecycleBin.Common.PredefinedType.String,
            PrimitiveTypeCode.IntPtr => preferNativeInt ? SyntaxRecycleBin.Common.IdentifierName.nint : SyntaxRecycleBin.Common.IdentifierName.IntPtr,
            PrimitiveTypeCode.UIntPtr => preferNativeInt ? SyntaxRecycleBin.Common.IdentifierName.nuint : SyntaxRecycleBin.Common.IdentifierName.UIntPtr,
            PrimitiveTypeCode.Void => SyntaxRecycleBin.Common.PredefinedType.Void,
            _ => throw new NotSupportedException("Unsupported type code: " + typeCode),
        };
    }
}
