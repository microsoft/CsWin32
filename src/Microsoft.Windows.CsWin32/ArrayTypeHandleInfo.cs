// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Metadata;
    using System.Runtime.InteropServices;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using static FastSyntaxFactory;

    internal record ArrayTypeHandleInfo(TypeHandleInfo ElementType, ArrayShape Shape) : TypeHandleInfo, ITypeHandleContainer
    {
        public override string ToString() => this.ToTypeSyntaxForDisplay().ToString();

        internal override TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, CustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes)
        {
            var element = this.ElementType.ToTypeSyntax(inputs, customAttributes);
            ArrayTypeSyntax arrayType = ArrayType(element.Type, SingletonList(ArrayRankSpecifier().AddSizes(this.Shape.Sizes.Select(size => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(size))).ToArray<ExpressionSyntax>())));
            MarshalAsAttribute? marshalAs = element.MarshalAsAttribute is object ? new MarshalAsAttribute(UnmanagedType.LPArray) { ArraySubType = element.MarshalAsAttribute.Value } : null;
            return new TypeSyntaxAndMarshaling(arrayType, marshalAs);
        }
    }
}
