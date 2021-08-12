// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Metadata;
    using System.Runtime.InteropServices;
    using Microsoft.CodeAnalysis.CSharp;
    using static FastSyntaxFactory;

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
                        ArrayType(elementSyntax).AddRankSpecifiers(ArrayRankSpecifier()),
                        marshalAs is object ? new MarshalAsAttribute(UnmanagedType.LPArray) { ArraySubType = marshalAs.Value } : new MarshalAsAttribute(UnmanagedType.LPArray));
                }
                else if (xIn || xOut)
                {
                    // But we can use a modifier to emulate a pointer and thereby enable marshaling.
                    return new TypeSyntaxAndMarshaling(elementSyntax, marshalAs)
                    {
                        ParameterModifier = Token(
                            xIn && xOut ? SyntaxKind.RefKeyword :
                            xIn ? SyntaxKind.InKeyword :
                            SyntaxKind.OutKeyword),
                    };
                }
                else if (inputs.Generator is object
                    && this.TryGetElementTypeDefinition(inputs.Generator, out TypeDefinition elementTypeDef)
                    && inputs.Generator?.IsDelegate(elementTypeDef) is true)
                {
                    return new TypeSyntaxAndMarshaling(inputs.Generator.FunctionPointer(elementTypeDef));
                }
                else
                {
                    // We can replace a pointer to a struct with a managed equivalent by changing the pointer to an array.
                    // We only want to enter this branch for struct fields, since method parameters can use in/out/ref modifiers.
                    return new TypeSyntaxAndMarshaling(
                        ArrayType(elementSyntax).AddRankSpecifiers(ArrayRankSpecifier()),
                        marshalAs is object ? new MarshalAsAttribute(UnmanagedType.LPArray) { ArraySubType = marshalAs.Value } : null);
                }
            }
            else if (inputs.AllowMarshaling && inputs.Generator is object
                && customAttributes?.Any(ah => MetadataUtilities.IsAttribute(inputs.Generator.Reader, inputs.Generator!.Reader.GetCustomAttribute(ah), Generator.InteropDecorationNamespace, "ComOutPtrAttribute")) is true)
            {
                return new TypeSyntaxAndMarshaling(PredefinedType(Token(SyntaxKind.ObjectKeyword)), new MarshalAsAttribute(UnmanagedType.IUnknown));
            }

            return new TypeSyntaxAndMarshaling(PointerType(elementSyntax));
        }

        private bool TryGetElementTypeDefinition(Generator generator, out TypeDefinition typeDef)
        {
            if (this.ElementType is HandleTypeHandleInfo handleElement)
            {
                if (handleElement.Handle.Kind == HandleKind.TypeReference)
                {
                    if (generator.TryGetTypeDefHandle((TypeReferenceHandle)handleElement.Handle, out TypeDefinitionHandle tdr))
                    {
                        typeDef = generator.Reader.GetTypeDefinition(tdr);
                        return true;
                    }
                }
                else if (handleElement.Handle.Kind == HandleKind.TypeDefinition)
                {
                    typeDef = generator.Reader.GetTypeDefinition((TypeDefinitionHandle)handleElement.Handle);
                    return true;
                }
            }

            typeDef = default;
            return false;
        }
    }
}
