// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

internal record PointerTypeHandleInfo(TypeHandleInfo ElementType) : TypeHandleInfo, ITypeHandleContainer
{
    public override string ToString() => this.ToTypeSyntaxForDisplay().ToString();

    internal override TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, Generator.GeneratingElement forElement, QualifiedCustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes = default)
    {
        Generator.NativeArrayInfo? nativeArrayInfo = customAttributes.HasValue ? customAttributes.Value.Generator.FindNativeArrayInfoAttribute(customAttributes.Value.Collection) : null;
        bool useComSourceGenerators = inputs.Generator?.UseSourceGenerators == true;

        // We can't marshal a pointer exposed as a field, unless it's a pointer to an array.
        if (inputs.AllowMarshaling && inputs.IsField && (customAttributes is null || nativeArrayInfo is null))
        {
            inputs = inputs with { AllowMarshaling = false };
        }

        if (inputs.AllowMarshaling && useComSourceGenerators
            && (this.ElementType is PointerTypeHandleInfo)
            && (nativeArrayInfo?.CountParamIndex is not null))
        {
            inputs = inputs with { AllowMarshaling = false };
        }

        bool xOptional = (parameterAttributes & ParameterAttributes.Optional) == ParameterAttributes.Optional;
        bool mustUsePointers = xOptional && forElement == Generator.GeneratingElement.InterfaceMember && nativeArrayInfo is null;
        mustUsePointers |= this.ElementType is HandleTypeHandleInfo handleElementType && handleElementType.Generator.IsStructWithFlexibleArray(handleElementType) is true;
        if (mustUsePointers)
        {
            // Disable marshaling because pointers to optional parameters cannot be passed by reference when used as parameters of a COM interface method.
            return new TypeSyntaxAndMarshaling(PointerType(this.ElementType.ToTypeSyntax(
                inputs with
                {
                    AllowMarshaling = false,
                    PreferInOutRef = false,
                },
                forElement,
                customAttributes,
                parameterAttributes).Type));
        }

        TypeSyntaxAndMarshaling elementTypeDetails = this.ElementType.ToTypeSyntax(inputs with { PreferInOutRef = false }, forElement, customAttributes, parameterAttributes);
        if (elementTypeDetails.MarshalAsAttribute is object ||
            elementTypeDetails.MarshalUsingType is string ||
            (inputs.Generator?.IsManagedType(this.ElementType) is true) || (inputs.PreferInOutRef && !xOptional && this.ElementType is PrimitiveTypeHandleInfo { PrimitiveTypeCode: not PrimitiveTypeCode.Void }))
        {
            bool xIn = (parameterAttributes & ParameterAttributes.In) == ParameterAttributes.In;
            bool xOut = (parameterAttributes & ParameterAttributes.Out) == ParameterAttributes.Out;

            // A pointer to a marshaled object is not allowed.
            if (inputs.AllowMarshaling && customAttributes.HasValue && nativeArrayInfo is not null)
            {
                // Source generators can't handle array of native delegates so generate as pointer.
                if (elementTypeDetails.Type is FunctionPointerTypeSyntax)
                {
                    // TODO: We can generate a custom marshaler for this scenario
                    return new TypeSyntaxAndMarshaling(PointerType(elementTypeDetails.Type));
                }

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
                    bool isComOutPtr = customAttributes?.Generator.FindInteropDecorativeAttribute(customAttributes.Value.Collection, "ComOutPtrAttribute").HasValue ?? false;
                    return new TypeSyntaxAndMarshaling(IdentifierName(nameof(IntPtr)))
                    {
                        ParameterModifier = Token(SyntaxKind.OutKeyword),
                    };
                }

                // But we can use a modifier to emulate a pointer and thereby enable marshaling.
                return new TypeSyntaxAndMarshaling(elementTypeDetails.Type, elementTypeDetails.MarshalAsAttribute, elementTypeDetails.NativeArrayInfo)
                {
                    MarshalUsingType = elementTypeDetails.MarshalUsingType,
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
        else if (inputs.AllowMarshaling && customAttributes is object && customAttributes.Value.Generator.FindInteropDecorativeAttribute(customAttributes.Value.Collection, "ComOutPtrAttribute") is not null)
        {
            if (useComSourceGenerators)
            {
                if (this.ElementType is HandleTypeHandleInfo handleElement && handleElement.IsType("BSTR"))
                {
                    return new TypeSyntaxAndMarshaling(PointerType(elementTypeDetails.Type));
                }
                else
                {
                    return new TypeSyntaxAndMarshaling(PredefinedType(Token(SyntaxKind.ObjectKeyword)), new MarshalAsAttribute(UnmanagedType.Interface), null);
                }
            }
            else
            {
                return new TypeSyntaxAndMarshaling(PredefinedType(Token(SyntaxKind.ObjectKeyword)), new MarshalAsAttribute(UnmanagedType.IUnknown), null);
            }
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
