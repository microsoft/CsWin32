// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

internal record PrimitiveTypeHandleInfo(PrimitiveTypeCode PrimitiveTypeCode) : TypeHandleInfo
{
    public override string ToString() => this.ToTypeSyntaxForDisplay().ToString();

    internal override TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, Generator.GeneratingElement forElement, QualifiedCustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes)
    {
        if (customAttributes?.Generator.FindAssociatedEnum(customAttributes.Value.Collection) is HandleTypeHandleInfo enumType)
        {
            if (customAttributes.Value.Generator.RequiresAssociatedEnumConversion(enumType, this.PrimitiveTypeCode)
                && forElement is Generator.GeneratingElement.ExternMethod or Generator.GeneratingElement.InterfaceAsStructMember)
            {
                return new TypeSyntaxAndMarshaling(ToTypeSyntax(this.PrimitiveTypeCode, inputs.PreferNativeInt));
            }

            customAttributes.Value.Generator.ValidateAssociatedEnumStorage(enumType, this.PrimitiveTypeCode);
            return new TypeSyntaxAndMarshaling(Generator.GetAssociatedEnumTypeSyntax(enumType, inputs, forElement));
        }
        else
        {
            if (inputs.AllowMarshaling && inputs.Generator?.UseSourceGenerators == true &&
                this.PrimitiveTypeCode == PrimitiveTypeCode.Boolean)
            {
                return new TypeSyntaxAndMarshaling(ToTypeSyntax(this.PrimitiveTypeCode, inputs.PreferNativeInt), new(UnmanagedType.Bool), null);
            }

            return new TypeSyntaxAndMarshaling(ToTypeSyntax(this.PrimitiveTypeCode, inputs.PreferNativeInt));
        }
    }

    internal override bool? IsValueType(TypeSyntaxSettings inputs)
    {
        return this.PrimitiveTypeCode is not (PrimitiveTypeCode.Object or PrimitiveTypeCode.Void);
    }

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
