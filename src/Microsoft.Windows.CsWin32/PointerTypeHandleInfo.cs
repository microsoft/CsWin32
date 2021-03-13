// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System;
    using System.Reflection;
    using System.Reflection.Metadata;
    using System.Runtime.InteropServices;
    using Microsoft.CodeAnalysis.CSharp;
    using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

    internal record PointerTypeHandleInfo(TypeHandleInfo ElementType) : TypeHandleInfo, ITypeHandleContainer
    {
        public override string ToString() => this.ToTypeSyntaxForDisplay().ToString();

        internal override TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, CustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes)
        {
            var (elementSyntax, marshalAs) = this.ElementType.ToTypeSyntax(inputs, customAttributes);
            if (marshalAs is object || inputs.Generator?.IsManagedType(this.ElementType) is true)
            {
                bool xIn = (parameterAttributes & ParameterAttributes.In) == ParameterAttributes.In;
                bool xOut = (parameterAttributes & ParameterAttributes.Out) == ParameterAttributes.Out;

                // A pointer to a marshaled object is not allowed.
                if (customAttributes.HasValue && inputs.Generator?.FindNativeArrayInfoAttribute(customAttributes.Value) is object)
                {
                    // But this pointer represents an array, so type as an array.
                    return new TypeSyntaxAndMarshaling(
                        xIn && !xOut ? Generator.MakeReadOnlySpanOfT(elementSyntax) : Generator.MakeSpanOfT(elementSyntax),
                        marshalAs is object ? new MarshalAsAttribute(UnmanagedType.LPArray) { ArraySubType = marshalAs.Value } : null);
                }
                else
                {
                    // But we can use a modifier to emulate a pointer and thereby enable marshaling.
                    return new TypeSyntaxAndMarshaling(elementSyntax, marshalAs)
                    {
                        ParameterModifier = Token(
                            xIn && xOut ? SyntaxKind.RefKeyword :
                            xIn ? SyntaxKind.InKeyword :
                            xOut ? SyntaxKind.OutKeyword :
                            throw new NotSupportedException("Pointer to marshaled value.")),
                    };
                }
            }

            return new TypeSyntaxAndMarshaling(PointerType(elementSyntax));
        }
    }
}
