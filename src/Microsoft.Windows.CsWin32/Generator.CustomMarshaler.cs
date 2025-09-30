// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    internal string RequestCustomMarshaler(string qualifiedEnumTypeName, UnmanagedType unmanagedType)
    {
        if (unmanagedType != UnmanagedType.U4)
        {
            throw new InvalidOperationException("Only UnmanagedType.U4 is supported for enum marshaling.");
        }

        if (!TrySplitPossiblyQualifiedName(qualifiedEnumTypeName, out string? @namespace, out string enumTypeName) ||
            !this.TryStripCommonNamespace(@namespace, out string? shortNamespace))
        {
            throw new InvalidOperationException($"This generator doesn't share a prefix with this enum {qualifiedEnumTypeName}");
        }

        string customTypeMarshalerName = $"{enumTypeName}To{unmanagedType}Marshaler";

        return this.volatileCode.GenerateCustomTypeMarshaler(customTypeMarshalerName, delegate
        {
            // Create type syntax for the enum type
            TypeSyntax enumTypeSyntax = IdentifierName(enumTypeName);

            // Create type syntax for the unmanaged type (uint for U4)
            TypeSyntax unmanagedTypeSyntax = PredefinedType(Token(SyntaxKind.UIntKeyword));

            // Create the CustomMarshaller attributes for all required marshal modes
            var marshalModes = new[]
            {
                "ManagedToUnmanagedIn",
                "ManagedToUnmanagedOut",
                "UnmanagedToManagedIn",
                "UnmanagedToManagedOut",
                "ElementIn",
                "ElementOut",
            };

            // [CustomMarshaller(typeof(Enum), MarshalMode.ManagedToUnmanagedIn, typeof(EnumToUintMarshaller))]
            // [CustomMarshaller(typeof(Enum), MarshalMode.ManagedToUnmanagedOut, typeof(EnumToUintMarshaller))]
            // [CustomMarshaller(typeof(Enum), MarshalMode.UnmanagedToManagedIn, typeof(EnumToUintMarshaller))]
            // [CustomMarshaller(typeof(Enum), MarshalMode.UnmanagedToManagedOut, typeof(EnumToUintMarshaller))]
            // [CustomMarshaller(typeof(Enum), MarshalMode.ElementIn, typeof(EnumToUintMarshaller))]
            // [CustomMarshaller(typeof(Enum), MarshalMode.ElementOut, typeof(EnumToUintMarshaller))]
            var customMarshallerAttributes = marshalModes.Select(mode =>
                Attribute(ParseName("global::System.Runtime.InteropServices.Marshalling.CustomMarshaller"))
                    .AddArgumentListArguments(
                        AttributeArgument(TypeOfExpression(enumTypeSyntax)),
                        AttributeArgument(MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            ParseName("global::System.Runtime.InteropServices.Marshalling.MarshalMode"),
                            IdentifierName(mode))),
                        AttributeArgument(TypeOfExpression(IdentifierName(customTypeMarshalerName)))))
                .ToArray();

            // Create ConvertToManaged method
            // public static unsafe Enum ConvertToManaged(uint unmanaged)
            // {
            //     return (Enum)unmanaged;
            // }
            MethodDeclarationSyntax convertToManagedMethod = MethodDeclaration(enumTypeSyntax, Identifier("ConvertToManaged"))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.UnsafeKeyword))
                .AddParameterListParameters(Parameter(Identifier("unmanaged")).WithType(unmanagedTypeSyntax.WithTrailingTrivia(Space)))
                .WithBody(Block().AddStatements(
                    ReturnStatement(UncheckedExpression(CastExpression(enumTypeSyntax, IdentifierName("unmanaged"))))));

            // Create ConvertToUnmanaged method
            // public static uint ConvertToUnmanaged(Enum managed)
            // {
            //     return (uint)managed;
            // }
            MethodDeclarationSyntax convertToUnmanagedMethod = MethodDeclaration(unmanagedTypeSyntax, Identifier("ConvertToUnmanaged"))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword))
                .AddParameterListParameters(Parameter(Identifier("managed")).WithType(enumTypeSyntax.WithTrailingTrivia(Space)))
                .WithBody(Block().AddStatements(
                    ReturnStatement(UncheckedExpression(CastExpression(unmanagedTypeSyntax, IdentifierName("managed"))))));

            // Create Free method
            // public static void Free(uint unmanaged)
            // {
            // }
            MethodDeclarationSyntax freeMethod = MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier("Free"))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword))
                .AddParameterListParameters(Parameter(Identifier("unmanaged")).WithType(unmanagedTypeSyntax.WithTrailingTrivia(Space)))
                .WithBody(Block()); // Empty body

            // Create the class declaration
            ClassDeclarationSyntax marshalerClass = ClassDeclaration(Identifier(customTypeMarshalerName))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword))
                .AddAttributeLists(customMarshallerAttributes.Select(attr => AttributeList().AddAttributes(attr)).ToArray())
                .AddMembers(convertToManagedMethod, convertToUnmanagedMethod, freeMethod);

            marshalerClass = marshalerClass.WithAdditionalAnnotations(new SyntaxAnnotation(NamespaceContainerAnnotation, shortNamespace));

            this.volatileCode.AddCustomTypeMarshaler(customTypeMarshalerName, marshalerClass);

            return marshalerClass;
        });
    }
}
