// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.Windows.CsWin32.FastSyntaxFactory;

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

    internal Generator.NativeArrayInfo? NativeArrayInfo { get; }

    internal SyntaxToken? ParameterModifier { get; init; }

    internal FieldDeclarationSyntax AddMarshalAs(FieldDeclarationSyntax fieldDeclaration)
    {
        return this.MarshalAsAttribute is object
            ? fieldDeclaration.AddAttributeLists(AttributeList().AddAttributes(Generator.MarshalAs(this.MarshalAsAttribute, this.NativeArrayInfo)))
            : fieldDeclaration;
    }

    internal ParameterSyntax AddMarshalAs(ParameterSyntax parameter)
    {
        return this.MarshalAsAttribute is object
            ? parameter.AddAttributeLists(AttributeList().AddAttributes(Generator.MarshalAs(this.MarshalAsAttribute, this.NativeArrayInfo)))
            : parameter;
    }

    internal MethodDeclarationSyntax AddReturnMarshalAs(MethodDeclarationSyntax methodDeclaration)
    {
        return this.MarshalAsAttribute is object
            ? methodDeclaration.AddAttributeLists(AttributeList().WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.ReturnKeyword))).AddAttributes(Generator.MarshalAs(this.MarshalAsAttribute, this.NativeArrayInfo)))
            : methodDeclaration;
    }

    internal DelegateDeclarationSyntax AddReturnMarshalAs(DelegateDeclarationSyntax methodDeclaration)
    {
        return this.MarshalAsAttribute is object
            ? methodDeclaration.AddAttributeLists(AttributeList().WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.ReturnKeyword))).AddAttributes(Generator.MarshalAs(this.MarshalAsAttribute, this.NativeArrayInfo)))
            : methodDeclaration;
    }

    internal TypeSyntax GetUnmarshaledType()
    {
        this.ThrowIfMarshallingRequired();
        return this.Type;
    }

    internal void ThrowIfMarshallingRequired()
    {
        if (this.MarshalAsAttribute is object)
        {
            throw new NotSupportedException("This type requires marshaling, but marshaling is not supported in this context.");
        }
    }
}
