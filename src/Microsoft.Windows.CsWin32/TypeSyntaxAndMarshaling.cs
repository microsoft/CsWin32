// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System;
    using System.Runtime.InteropServices;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using static FastSyntaxFactory;

    internal struct TypeSyntaxAndMarshaling
    {
        internal TypeSyntaxAndMarshaling(TypeSyntax type, MarshalAsAttribute? marshalAs = null)
        {
            this.Type = type;
            this.MarshalAsAttribute = marshalAs;
            this.ParameterModifier = null;
        }

        internal TypeSyntax Type { get; init; }

        internal MarshalAsAttribute? MarshalAsAttribute { get; init; }

        internal SyntaxToken? ParameterModifier { get; init; }

        internal void Deconstruct(out TypeSyntax type, out MarshalAsAttribute? marshalAs)
        {
            type = this.Type;
            marshalAs = this.MarshalAsAttribute;
        }

        internal FieldDeclarationSyntax AddMarshalAs(FieldDeclarationSyntax fieldDeclaration)
        {
            return this.MarshalAsAttribute is object
                ? fieldDeclaration.AddAttributeLists(AttributeList().AddAttributes(Generator.MarshalAs(this.MarshalAsAttribute)))
                : fieldDeclaration;
        }

        internal ParameterSyntax AddMarshalAs(ParameterSyntax parameter)
        {
            return this.MarshalAsAttribute is object
                ? parameter.AddAttributeLists(AttributeList().AddAttributes(Generator.MarshalAs(this.MarshalAsAttribute)))
                : parameter;
        }

        internal MethodDeclarationSyntax AddReturnMarshalAs(MethodDeclarationSyntax methodDeclaration)
        {
            return this.MarshalAsAttribute is object
                ? methodDeclaration.AddAttributeLists(AttributeList().WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.ReturnKeyword))).AddAttributes(Generator.MarshalAs(this.MarshalAsAttribute)))
                : methodDeclaration;
        }

        internal DelegateDeclarationSyntax AddReturnMarshalAs(DelegateDeclarationSyntax methodDeclaration)
        {
            return this.MarshalAsAttribute is object
                ? methodDeclaration.AddAttributeLists(AttributeList().WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.ReturnKeyword))).AddAttributes(Generator.MarshalAs(this.MarshalAsAttribute)))
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
}
