// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.Windows.CsWin32.FastSyntaxFactory;

namespace Microsoft.Windows.CsWin32;

internal record PointerTypeHandleInfo(TypeHandleInfo ElementType) : TypeHandleInfo, ITypeHandleContainer
{
    public override string ToString() => this.ToTypeSyntaxForDisplay().ToString();

    internal override TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, CustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes)
    {
        TypeSyntaxAndMarshaling elementTypeDetails = this.ElementType.ToTypeSyntax(inputs, customAttributes);
        if (elementTypeDetails.MarshalAsAttribute is object || inputs.Generator?.IsManagedType(this.ElementType) is true)
        {
            bool xIn = (parameterAttributes & ParameterAttributes.In) == ParameterAttributes.In;
            bool xOut = (parameterAttributes & ParameterAttributes.Out) == ParameterAttributes.Out;

            // A pointer to a marshaled object is not allowed.
            if (inputs.AllowMarshaling && customAttributes.HasValue && inputs.Generator?.FindNativeArrayInfoAttribute(customAttributes.Value) is { } nativeArrayInfo)
            {
                // But this pointer represents an array, so type as an array.
                MarshalAsAttribute marshalAsAttribute = new MarshalAsAttribute(UnmanagedType.LPArray);
                if (elementTypeDetails.MarshalAsAttribute is object)
                {
                    marshalAsAttribute.ArraySubType = elementTypeDetails.MarshalAsAttribute.Value;
                }

                return new TypeSyntaxAndMarshaling(ArrayType(elementTypeDetails.Type).AddRankSpecifiers(ArrayRankSpecifier()), marshalAsAttribute, nativeArrayInfo);
            }
            else if (xIn || xOut)
            {
                if (elementTypeDetails.Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.ObjectKeyword } } && inputs.Generator is not null && inputs.Generator.Options.ComInterop.UseIntPtrForComOutPointers)
                {
                    bool isComOutPtr = inputs.Generator.FindInteropDecorativeAttribute(customAttributes, "ComOutPtrAttribute").HasValue;
                    return new TypeSyntaxAndMarshaling(IdentifierName(nameof(IntPtr)))
                    {
                        ParameterModifier = Token(SyntaxKind.OutKeyword),
                    };
                }

                // But we can use a modifier to emulate a pointer and thereby enable marshaling.
                return new TypeSyntaxAndMarshaling(elementTypeDetails.Type, elementTypeDetails.MarshalAsAttribute, elementTypeDetails.NativeArrayInfo)
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
            else if (inputs.AllowMarshaling)
            {
                // We can replace a pointer to a struct with a managed equivalent by changing the pointer to an array.
                // We only want to enter this branch for struct fields, since method parameters can use in/out/ref modifiers.
                return new TypeSyntaxAndMarshaling(
                    ArrayType(elementTypeDetails.Type).AddRankSpecifiers(ArrayRankSpecifier()),
                    elementTypeDetails.MarshalAsAttribute is object ? new MarshalAsAttribute(UnmanagedType.LPArray) { ArraySubType = elementTypeDetails.MarshalAsAttribute.Value } : null,
                    elementTypeDetails.NativeArrayInfo);
            }
        }
        else if (inputs.AllowMarshaling && inputs.Generator?.FindInteropDecorativeAttribute(customAttributes, "ComOutPtrAttribute") is not null)
        {
            return new TypeSyntaxAndMarshaling(PredefinedType(Token(SyntaxKind.ObjectKeyword)), new MarshalAsAttribute(UnmanagedType.IUnknown), null);
        }

        // Since we'll be using pointers, we have to ensure the element type does not require any marshaling.
        if (inputs.AllowMarshaling)
        {
            // Evidently all tests pass without actually doing this, so we'll leave it out for now.
            ////elementTypeDetails = this.ElementType.ToTypeSyntax(inputs with { AllowMarshaling = false }, customAttributes);
        }

        return new TypeSyntaxAndMarshaling(PointerType(elementTypeDetails.Type));
    }

    internal override bool? IsValueType(TypeSyntaxSettings inputs) => false;

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
