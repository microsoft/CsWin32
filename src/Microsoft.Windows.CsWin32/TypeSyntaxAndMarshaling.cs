// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

internal struct TypeSyntaxAndMarshaling
{
    internal TypeSyntaxAndMarshaling(TypeSyntax type)
    {
        this.Type = type;
        this.MarshalAsAttribute = null;
        this.NativeArrayInfo = null;
        this.ParameterModifier = null;
    }

    internal TypeSyntaxAndMarshaling(TypeSyntax type, MarshalAsAttribute? marshalAs, Generator.NativeArrayInfo? nativeArrayInfo)
    {
        this.Type = type;
        this.MarshalAsAttribute = marshalAs;
        this.NativeArrayInfo = nativeArrayInfo;
        this.ParameterModifier = null;
    }

    internal TypeSyntax Type { get; init; }

    internal MarshalAsAttribute? MarshalAsAttribute { get; init; }

    internal string? MarshalUsingType { get; init; }

    internal Generator.NativeArrayInfo? NativeArrayInfo { get; }

    internal SyntaxToken? ParameterModifier { get; init; }

    internal FieldDeclarationSyntax AddMarshalAs(FieldDeclarationSyntax fieldDeclaration)
    {
        return this.MarshalAsAttribute is object
            ? fieldDeclaration.AddAttributeLists(AttributeList(MarshalAs(this.MarshalAsAttribute, this.NativeArrayInfo)))
            : fieldDeclaration;
    }

    internal ParameterSyntax AddMarshalAs(ParameterSyntax parameter)
    {
        return this.MarshalAsAttribute is object
            ? parameter.AddAttributeLists(AttributeList(MarshalAs(this.MarshalAsAttribute, this.NativeArrayInfo)))
            : parameter;
    }

    internal MethodDeclarationSyntax AddReturnMarshalAs(MethodDeclarationSyntax methodDeclaration)
    {
        return this.GetMarshalingAttribute() is AttributeSyntax attribute
            ? methodDeclaration.AddAttributeLists(AttributeList(attribute).WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.ReturnKeyword))))
            : methodDeclaration;
    }

    internal DelegateDeclarationSyntax AddReturnMarshalAs(DelegateDeclarationSyntax methodDeclaration)
    {
        return this.GetMarshalingAttribute() is AttributeSyntax attribute
            ? methodDeclaration.AddAttributeLists(AttributeList(attribute).WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.ReturnKeyword))))
            : methodDeclaration;
    }

    internal TypeSyntax GetUnmarshaledType()
    {
        this.ThrowIfMarshallingRequired();
        return this.Type;
    }

    internal void ThrowIfMarshallingRequired()
    {
        if (this.MarshalAsAttribute is object || this.MarshalUsingType is not null)
        {
            throw new NotSupportedException("This type requires marshaling, but marshaling is not supported in this context.");
        }
    }

    /// <summary>
    /// Gets the runtime or source-generated marshaling attribute for this value.
    /// </summary>
    /// <returns>The marshaling attribute, or null for a native representation.</returns>
    internal AttributeSyntax? GetMarshalingAttribute() => this.MarshalUsingType is string marshaler
        ? Attribute(ParseName("global::System.Runtime.InteropServices.Marshalling.MarshalUsing"))
            .AddArgumentListArguments(AttributeArgument(TypeOfExpression(ParseName(marshaler))))
        : MarshalAs(this.MarshalAsAttribute, this.NativeArrayInfo);
}
