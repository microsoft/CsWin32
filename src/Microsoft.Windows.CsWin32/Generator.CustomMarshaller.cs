// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    internal string RequestCustomTypeDefMarshaller(string fullyQualifiedTypeName, TypeSyntax unmanagedTypeSyntax)
    {
        if (!TrySplitPossiblyQualifiedName(fullyQualifiedTypeName, out string? @namespace, out string typeDefName) ||
            !this.TryStripCommonNamespace(@namespace, out string? shortNamespace))
        {
            throw new InvalidOperationException($"This generator doesn't share a prefix with this enum {fullyQualifiedTypeName}");
        }

        // Custom marshallers should go in a InteropServices sub-namespace.
        shortNamespace = shortNamespace.Length > 0 ? $"{shortNamespace}.InteropServices" : "InteropServices";

        string customTypeMarshallerName = $"{typeDefName}Marshaller";

        return this.volatileCode.GenerateCustomTypeMarshaller(customTypeMarshallerName, delegate
        {
            // Type syntax for the typedef type (unqualified within its namespace container).
            TypeSyntax typedefTypeSyntax = ParseName(fullyQualifiedTypeName);

            // Single CustomMarshaller attribute for MarshalMode.Default.
            AttributeSyntax attribute = Attribute(ParseName("global::System.Runtime.InteropServices.Marshalling.CustomMarshaller"))
                .AddArgumentListArguments(
                    AttributeArgument(TypeOfExpression(typedefTypeSyntax)),
                    AttributeArgument(MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        ParseName("global::System.Runtime.InteropServices.Marshalling.MarshalMode"),
                        IdentifierName("Default"))),
                    AttributeArgument(TypeOfExpression(IdentifierName(customTypeMarshallerName))));

            // public static unsafe void* ConvertToUnmanaged(HWND managed) => managed.Value;
            MethodDeclarationSyntax toUnmanaged = MethodDeclaration(unmanagedTypeSyntax, Identifier("ConvertToUnmanaged"))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.UnsafeKeyword))
                .AddParameterListParameters(Parameter(typedefTypeSyntax.WithTrailingTrivia(Space), Identifier("managed")))
                .WithBody(Block(ReturnStatement(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("managed"), IdentifierName("Value")))));

            // public static unsafe HWND ConvertToManaged(void* unmanaged) => new(unmanaged);
            MethodDeclarationSyntax toManaged = MethodDeclaration(typedefTypeSyntax, Identifier("ConvertToManaged"))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.UnsafeKeyword))
                .AddParameterListParameters(Parameter(unmanagedTypeSyntax.WithTrailingTrivia(Space), Identifier("unmanaged")))
                .WithBody(Block(ReturnStatement(ObjectCreationExpression(typedefTypeSyntax, [Argument(IdentifierName("unmanaged"))]))));

            ClassDeclarationSyntax marshallerClass = ClassDeclaration(Identifier(customTypeMarshallerName), [toUnmanaged, toManaged])
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword))
                .AddAttributeLists(AttributeList(attribute))
                .WithAdditionalAnnotations(new SyntaxAnnotation(NamespaceContainerAnnotation, shortNamespace));

            string qualifiedName = $"global::{this.Namespace}.{shortNamespace}.{customTypeMarshallerName}";
            CustomMarshallerTypeRecord typeRecord = new(marshallerClass, qualifiedName);
            this.volatileCode.AddCustomTypeMarshaller(customTypeMarshallerName, typeRecord);
            return typeRecord;
        });
    }

    internal string RequestCustomWinRTMarshaller(string qualifiedWinRTTypeName)
    {
        if (!TrySplitPossiblyQualifiedName(qualifiedWinRTTypeName, out string? @namespace, out string winrtTypeName))
        {
            // If no namespace, use the type name as-is
            winrtTypeName = qualifiedWinRTTypeName;
            @namespace = string.Empty;
        }

        string customTypeMarshallerName = $"WinRTMarshaller{winrtTypeName}";

        string marshallerNamespace = "CsWin32.InteropServices";

        return this.volatileCode.GenerateCustomTypeMarshaller(customTypeMarshallerName, delegate
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
                        AttributeArgument(TypeOfExpression(IdentifierName(customTypeMarshallerName)))))
                .ToArray();

            // Create ConvertToManaged method
            // public static unsafe T ConvertToManaged(nint unmanaged)
            // {
            //     return global::WinRT.MarshalInterface<T>.FromAbi(unmanaged);
            // }
            MethodDeclarationSyntax convertToManagedMethod = MethodDeclaration(winrtTypeSyntax, Identifier("ConvertToManaged"))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.UnsafeKeyword))
                .AddParameterListParameters(Parameter(unmanagedTypeSyntax.WithTrailingTrivia(Space), Identifier("unmanaged")))
                .WithBody(Block(
                    ReturnStatement(InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            GenericName("global::WinRT.MarshalInterface", [winrtTypeSyntax]),
                            IdentifierName("FromAbi")),
                        [Argument(IdentifierName("unmanaged"))]))));

            // Create ConvertToUnmanaged method
            // public static nint ConvertToUnmanaged(T managed)
            // {
            //     return global::WinRT.MarshalInterface<T>.FromManaged(managed);
            // }
            MethodDeclarationSyntax convertToUnmanagedMethod = MethodDeclaration(unmanagedTypeSyntax, Identifier("ConvertToUnmanaged"))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword))
                .AddParameterListParameters(Parameter(winrtTypeSyntax.WithTrailingTrivia(Space), Identifier("managed")))
                .WithBody(Block(
                    ReturnStatement(InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            GenericName("global::WinRT.MarshalInterface", [winrtTypeSyntax]),
                            IdentifierName("FromManaged")),
                        [Argument(IdentifierName("managed"))]))));

            // Create Free method
            // public static void Free(nint unmanaged)
            // {
            //     global::WinRT.MarshalInterface<T>.DisposeAbi(unmanaged);
            // }
            MethodDeclarationSyntax freeMethod = MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier("Free"))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword))
                .AddParameterListParameters(Parameter(unmanagedTypeSyntax.WithTrailingTrivia(Space), Identifier("unmanaged")))
                .WithBody(Block(
                    ExpressionStatement(InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            GenericName("global::WinRT.MarshalInterface", [winrtTypeSyntax]),
                            IdentifierName("DisposeAbi")),
                        [Argument(IdentifierName("unmanaged"))]))));

            // Create the class declaration
            ClassDeclarationSyntax marshallerClass = ClassDeclaration(Identifier(customTypeMarshallerName), [convertToManagedMethod, convertToUnmanagedMethod, freeMethod])
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword))
                .AddAttributeLists(customMarshallerAttributes.Select(attr => AttributeList(attr)).ToArray());

            marshallerClass = marshallerClass.WithAdditionalAnnotations(new SyntaxAnnotation(NamespaceContainerAnnotation, marshallerNamespace));

            string qualifiedName = $"global::{this.Namespace}.{marshallerNamespace}.{customTypeMarshallerName}";

            CustomMarshallerTypeRecord typeRecord = new(marshallerClass, qualifiedName);

            // For WinRT types, we generally don't need a specific namespace container annotation
            // since they're typically in the global namespace context
            this.volatileCode.AddCustomTypeMarshaller(customTypeMarshallerName, typeRecord);

            return typeRecord;
        });
    }

    private record CustomMarshallerTypeRecord(ClassDeclarationSyntax ClassDeclaration, string QualifiedName);
}
