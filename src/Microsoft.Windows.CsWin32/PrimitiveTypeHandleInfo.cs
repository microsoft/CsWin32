// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

internal record PrimitiveTypeHandleInfo(PrimitiveTypeCode PrimitiveTypeCode) : TypeHandleInfo
{
    public override string ToString() => this.ToTypeSyntaxForDisplay().ToString();

    internal override TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, Generator.GeneratingElement forElement, QualifiedCustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes)
    {
        // We want to expose the enum type when there is one, but doing it *properly* requires marshaling to the underlying type.
        // The length of the enum may differ from the length of the primitive type, which means we can't just use the enum type directly.
        // Even if the lengths match, if the enum's underlying base type conflicts with the primitive's signed type (e.g. int != uint),
        // certain CPU architectures can fail as well.
        // So we just have to use the primitive type when marshaling is not allowed.
        if (inputs.AllowMarshaling && customAttributes?.Generator.FindAssociatedEnum(customAttributes.Value.Collection) is NameSyntax enumTypeName && inputs.Generator!.TryGenerateType(enumTypeName.ToString(), out IReadOnlyCollection<string> preciseMatch))
        {
            // Use the qualified name.
            enumTypeName = ParseName(Generator.ReplaceCommonNamespaceWithAlias(inputs.Generator, preciseMatch.First()));
            UnmanagedType unmanagedType = GetUnmanagedType(this.PrimitiveTypeCode);

            // If marshaling using source generators, we need to generate a custom marshaler and a MarshalUsing(...) attribute.
            if (inputs.AllowMarshaling && inputs.Generator?.UseSourceGenerators == true)
            {
                string marshalerTypeName = inputs.Generator!.RequestCustomEnumMarshaler(preciseMatch.First(), unmanagedType);
                return new TypeSyntaxAndMarshaling(enumTypeName) { MarshalUsingType = marshalerTypeName };
            }

            MarshalAsAttribute marshalAs = new(unmanagedType);
            return new TypeSyntaxAndMarshaling(enumTypeName, marshalAs, nativeArrayInfo: null);
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
        return this.PrimitiveTypeCode is not PrimitiveTypeCode.Object or PrimitiveTypeCode.Void;
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

    private static UnmanagedType GetUnmanagedType(PrimitiveTypeCode typeCode)
    {
        return typeCode switch
        {
            PrimitiveTypeCode.SByte => UnmanagedType.I1,
            PrimitiveTypeCode.Byte => UnmanagedType.U1,
            PrimitiveTypeCode.Int16 => UnmanagedType.I2,
            PrimitiveTypeCode.UInt16 => UnmanagedType.U2,
            PrimitiveTypeCode.Int32 => UnmanagedType.I4,
            PrimitiveTypeCode.UInt32 => UnmanagedType.U4,
            PrimitiveTypeCode.Int64 => UnmanagedType.I8,
            PrimitiveTypeCode.UInt64 => UnmanagedType.U8,
            _ => throw new NotSupportedException("Unsupported type code: " + typeCode),
        };
    }
}
