// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    internal string RequestCustomEnumMarshaler(string qualifiedEnumTypeName, UnmanagedType unmanagedType)
    {
        // Create type syntax for the unmanaged type (uint for U4)
        TypeSyntax unmanagedTypeSyntax = unmanagedType switch
        {
            UnmanagedType.U4 => PredefinedType(Token(SyntaxKind.UIntKeyword)),
            UnmanagedType.I4 => PredefinedType(Token(SyntaxKind.IntKeyword)),
            _ => throw new InvalidOperationException($"Unsupported unmanaged type: {unmanagedType}"),
        };

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

            string qualifiedName = $"global::{this.Namespace}.{shortNamespace}.{customTypeMarshalerName}";

            CustomMarshalerTypeRecord typeRecord = new(marshalerClass, qualifiedName);

            this.volatileCode.AddCustomTypeMarshaler(customTypeMarshalerName, typeRecord);

            return typeRecord;
        });
    }

    internal string RequestCustomWinRTMarshaler(string qualifiedWinRTTypeName)
    {
        if (!TrySplitPossiblyQualifiedName(qualifiedWinRTTypeName, out string? @namespace, out string winrtTypeName))
        {
            // If no namespace, use the type name as-is
            winrtTypeName = qualifiedWinRTTypeName;
            @namespace = string.Empty;
        }

        string customTypeMarshalerName = $"WinRTMarshaler{winrtTypeName}";

        string marshalerNamespace = "CsWin32.InteropServices";

        return this.volatileCode.GenerateCustomTypeMarshaler(customTypeMarshalerName, delegate
        {
            // Create type syntax for the WinRT type (using the qualified name)
            TypeSyntax winrtTypeSyntax = string.IsNullOrEmpty(@namespace)
                ? IdentifierName(winrtTypeName)
                : QualifiedName(ParseName(@namespace), IdentifierName(winrtTypeName));

            // Create type syntax for the unmanaged type (nint)
            TypeSyntax unmanagedTypeSyntax = IdentifierName("nint");

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

            var customMarshallerAttributes = marshalModes.Select(mode =>
                Attribute(ParseName("global::System.Runtime.InteropServices.Marshalling.CustomMarshaller"))
                    .AddArgumentListArguments(
                        AttributeArgument(TypeOfExpression(winrtTypeSyntax)),
                        AttributeArgument(MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            ParseName("global::System.Runtime.InteropServices.Marshalling.MarshalMode"),
                            IdentifierName(mode))),
                        AttributeArgument(TypeOfExpression(IdentifierName(customTypeMarshalerName)))))
                .ToArray();

            // Create ConvertToManaged method
            // public static unsafe T ConvertToManaged(nint unmanaged)
            // {
            //     return global::WinRT.MarshalInterface<T>.FromAbi(unmanaged);
            // }
            MethodDeclarationSyntax convertToManagedMethod = MethodDeclaration(winrtTypeSyntax, Identifier("ConvertToManaged"))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.UnsafeKeyword))
                .AddParameterListParameters(Parameter(Identifier("unmanaged")).WithType(unmanagedTypeSyntax.WithTrailingTrivia(Space)))
                .WithBody(Block().AddStatements(
                    ReturnStatement(InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            GenericName("global::WinRT.MarshalInterface")
                                .AddTypeArgumentListArguments(winrtTypeSyntax),
                            IdentifierName("FromAbi")),
                        ArgumentList().AddArguments(Argument(IdentifierName("unmanaged")))))));

            // Create ConvertToUnmanaged method
            // public static nint ConvertToUnmanaged(T managed)
            // {
            //     return global::WinRT.MarshalInterface<T>.FromManaged(managed);
            // }
            MethodDeclarationSyntax convertToUnmanagedMethod = MethodDeclaration(unmanagedTypeSyntax, Identifier("ConvertToUnmanaged"))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword))
                .AddParameterListParameters(Parameter(Identifier("managed")).WithType(winrtTypeSyntax.WithTrailingTrivia(Space)))
                .WithBody(Block().AddStatements(
                    ReturnStatement(InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            GenericName("global::WinRT.MarshalInterface")
                                .AddTypeArgumentListArguments(winrtTypeSyntax),
                            IdentifierName("FromManaged")),
                        ArgumentList().AddArguments(Argument(IdentifierName("managed")))))));

            // Create Free method
            // public static void Free(nint unmanaged)
            // {
            //     global::WinRT.MarshalInterface<T>.DisposeAbi(unmanaged);
            // }
            MethodDeclarationSyntax freeMethod = MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier("Free"))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword))
                .AddParameterListParameters(Parameter(Identifier("unmanaged")).WithType(unmanagedTypeSyntax.WithTrailingTrivia(Space)))
                .WithBody(Block().AddStatements(
                    ExpressionStatement(InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            GenericName("global::WinRT.MarshalInterface")
                                .AddTypeArgumentListArguments(winrtTypeSyntax),
                            IdentifierName("DisposeAbi")),
                        ArgumentList().AddArguments(Argument(IdentifierName("unmanaged")))))));

            // Create the class declaration
            ClassDeclarationSyntax marshalerClass = ClassDeclaration(Identifier(customTypeMarshalerName))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword))
                .AddAttributeLists(customMarshallerAttributes.Select(attr => AttributeList().AddAttributes(attr)).ToArray())
                .AddMembers(convertToManagedMethod, convertToUnmanagedMethod, freeMethod);

            marshalerClass = marshalerClass.WithAdditionalAnnotations(new SyntaxAnnotation(NamespaceContainerAnnotation, marshalerNamespace));

            string qualifiedName = $"global::{this.Namespace}.{marshalerNamespace}.{customTypeMarshalerName}";

            CustomMarshalerTypeRecord typeRecord = new(marshalerClass, qualifiedName);

            // For WinRT types, we generally don't need a specific namespace container annotation
            // since they're typically in the global namespace context
            this.volatileCode.AddCustomTypeMarshaler(customTypeMarshalerName, typeRecord);

            return typeRecord;
        });
    }

    private record CustomMarshalerTypeRecord(ClassDeclarationSyntax ClassDeclaration, string QualifiedName);
}
