// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

internal record ArrayTypeHandleInfo(TypeHandleInfo ElementType, ArrayShape Shape) : TypeHandleInfo, ITypeHandleContainer
{
    public override string ToString() => this.ToTypeSyntaxForDisplay().ToString();

    internal override TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, Generator.GeneratingElement forElement, QualifiedCustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes)
    {
        bool useSourceGenerators = inputs.Generator?.UseSourceGenerators == true;

        // If this is a field, we need to emit as blittable if using source generators.
        if (useSourceGenerators && inputs.IsField)
        {
            inputs = inputs with { AllowMarshaling = false };
        }

        TypeSyntaxAndMarshaling element = this.ElementType.ToTypeSyntax(inputs, forElement, customAttributes);
        if (inputs.AllowMarshaling || inputs.IsField)
        {
            ArrayTypeSyntax arrayType = ArrayType(element.Type, [ArrayRankSpecifier([.. this.Shape.Sizes.Select(size => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(size)))])]);
            MarshalAsAttribute? marshalAs = element.MarshalAsAttribute is object ? new MarshalAsAttribute(UnmanagedType.LPArray) { ArraySubType = element.MarshalAsAttribute.Value } : null;
            return new TypeSyntaxAndMarshaling(arrayType, marshalAs, element.NativeArrayInfo);
        }
        else
        {
            return new TypeSyntaxAndMarshaling(PointerType(element.Type));
        }
    }

    internal override bool? IsValueType(TypeSyntaxSettings inputs) => false;
}
