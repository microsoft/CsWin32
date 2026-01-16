// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    internal static bool IsUntypedDelegate(MetadataReader reader, TypeDefinition typeDef) => reader.StringComparer.Equals(typeDef.Name, "PROC") || reader.StringComparer.Equals(typeDef.Name, "FARPROC");

    internal FunctionPointerTypeSyntax FunctionPointer(QualifiedTypeDefinition delegateType)
    {
        if (delegateType.Generator != this)
        {
            FunctionPointerTypeSyntax? result = null;
            delegateType.Generator.volatileCode.GenerationTransaction(() => result = delegateType.Generator.FunctionPointer(delegateType.Definition));
            return result!;
        }
        else
        {
            return this.FunctionPointer(delegateType.Definition);
        }
    }

    internal FunctionPointerTypeSyntax FunctionPointer(TypeDefinition delegateType)
    {
        CustomAttribute ufpAtt = this.FindAttribute(delegateType.GetCustomAttributes(), SystemRuntimeInteropServices, nameof(UnmanagedFunctionPointerAttribute))!.Value;
        CustomAttributeValue<TypeSyntax> attArgs = ufpAtt.DecodeValue(CustomAttributeTypeProvider.Instance);
        var callingConvention = (CallingConvention)attArgs.FixedArguments[0].Value!;

        this.GetSignatureForDelegate(delegateType, out MethodDefinition invokeMethodDef, out MethodSignature<TypeHandleInfo> signature, out CustomAttributeHandleCollection? returnTypeAttributes);
        if (this.FindAttribute(returnTypeAttributes, SystemRuntimeInteropServices, nameof(MarshalAsAttribute)).HasValue)
        {
            throw new NotSupportedException("Marshaling is not supported for function pointers.");
        }

        return this.FunctionPointer(invokeMethodDef, signature);
    }

    private DelegateDeclarationSyntax DeclareDelegate(TypeDefinition typeDef)
    {
        if (!this.options.AllowMarshaling)
        {
            throw new NotSupportedException("Delegates are not declared while in all-structs mode.");
        }

        string name = this.Reader.GetString(typeDef.Name);
        TypeSyntaxSettings typeSettings = this.delegateSignatureTypeSettings;

        CallingConvention? callingConvention = null;
        if (this.FindAttribute(typeDef.GetCustomAttributes(), SystemRuntimeInteropServices, nameof(UnmanagedFunctionPointerAttribute)) is CustomAttribute att)
        {
            CustomAttributeValue<TypeSyntax> args = att.DecodeValue(CustomAttributeTypeProvider.Instance);
            callingConvention = (CallingConvention)(int)args.FixedArguments[0].Value!;
        }

        this.GetSignatureForDelegate(typeDef, out MethodDefinition invokeMethodDef, out MethodSignature<TypeHandleInfo> signature, out CustomAttributeHandleCollection? returnTypeAttributes);
        TypeSyntaxAndMarshaling returnValue = signature.ReturnType.ToTypeSyntax(typeSettings, GeneratingElement.Delegate, returnTypeAttributes?.QualifyWith(this));

        DelegateDeclarationSyntax result = DelegateDeclaration(returnValue.Type, Identifier(name))
            .WithParameterList(FixTrivia(this.CreateParameterList(invokeMethodDef, signature, typeSettings, GeneratingElement.Delegate)))
            .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword));
        result = returnValue.AddReturnMarshalAs(result);

        if (callingConvention.HasValue)
        {
            result = result.AddAttributeLists(AttributeList(UnmanagedFunctionPointer(callingConvention.Value)).WithCloseBracketToken(TokenWithLineFeed(SyntaxKind.CloseBracketToken)));
        }

        return result;
    }

    private MemberDeclarationSyntax DeclareUntypedDelegate(TypeDefinition typeDef)
    {
        IdentifierNameSyntax name = IdentifierName(this.Reader.GetString(typeDef.Name));
        IdentifierNameSyntax valueFieldName = IdentifierName("Value");

        // internal IntPtr Value;
        FieldDeclarationSyntax valueField = FieldDeclaration(
            [TokenWithSpace(this.Visibility)],
            VariableDeclaration(IntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space)), [VariableDeclarator(valueFieldName.Identifier)]));

        // internal T CreateDelegate<T>() => Marshal.GetDelegateForFunctionPointer<T>(this.Value);
        IdentifierNameSyntax typeParameter = IdentifierName("TDelegate");
        MemberAccessExpressionSyntax methodToCall = this.getDelegateForFunctionPointerGenericExists
            ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Marshal)), GenericName(nameof(Marshal.GetDelegateForFunctionPointer), [typeParameter]))
            : MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Marshal)), IdentifierName(nameof(Marshal.GetDelegateForFunctionPointer)));
        ArgumentListSyntax arguments = ArgumentList(Argument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), valueFieldName)));
        if (!this.getDelegateForFunctionPointerGenericExists)
        {
            arguments = arguments.AddArguments(Argument(TypeOfExpression(typeParameter)));
        }

        ExpressionSyntax bodyExpression = InvocationExpression(methodToCall, arguments);
        if (!this.getDelegateForFunctionPointerGenericExists)
        {
            bodyExpression = CastExpression(typeParameter, bodyExpression);
        }

        MethodDeclarationSyntax createDelegateMethod = MethodDeclaration(typeParameter, Identifier("CreateDelegate"))
            .AddTypeParameterListParameters(TypeParameter(typeParameter.Identifier))
            .AddConstraintClauses(TypeParameterConstraintClause(typeParameter, [TypeConstraint(IdentifierName("Delegate"))]))
            .WithExpressionBody(ArrowExpressionClause(bodyExpression))
            .AddModifiers(TokenWithSpace(this.Visibility))
            .WithSemicolonToken(SemicolonWithLineFeed);

        StructDeclarationSyntax typedefStruct = StructDeclaration(
            name.Identifier,
            [
                valueField,
                .. this.CreateCommonTypeDefMembers(name, IntPtrTypeSyntax, valueFieldName),
                createDelegateMethod
            ])
            .WithModifiers([TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.PartialKeyword)]);
        return typedefStruct;
    }

    private void GetSignatureForDelegate(TypeDefinition typeDef, out MethodDefinition invokeMethodDef, out MethodSignature<TypeHandleInfo> signature, out CustomAttributeHandleCollection? returnTypeAttributes)
    {
        invokeMethodDef = typeDef.GetMethods().Select(this.Reader.GetMethodDefinition).Single(def => this.Reader.StringComparer.Equals(def.Name, "Invoke"));
        signature = invokeMethodDef.DecodeSignature(this.SignatureHandleProvider, null);
        returnTypeAttributes = this.GetReturnTypeCustomAttributes(invokeMethodDef);
    }

    private FunctionPointerTypeSyntax FunctionPointer(MethodDefinition methodDefinition, MethodSignature<TypeHandleInfo> signature)
    {
        FunctionPointerCallingConventionSyntax callingConventionSyntax = FunctionPointerCallingConvention(
            Token(SyntaxKind.UnmanagedKeyword),
            FunctionPointerUnmanagedCallingConventionList(ToUnmanagedCallingConventionSyntax(CallingConvention.StdCall)));

        FunctionPointerParameterListSyntax parametersList = FunctionPointerParameterList();

        foreach (ParameterHandle parameterHandle in methodDefinition.GetParameters())
        {
            Parameter parameter = this.Reader.GetParameter(parameterHandle);
            if (parameter.SequenceNumber == 0)
            {
                continue;
            }

            TypeHandleInfo? parameterTypeInfo = signature.ParameterTypes[parameter.SequenceNumber - 1];
            parametersList = parametersList.AddParameters(this.TranslateDelegateToFunctionPointer(parameterTypeInfo, parameter.GetCustomAttributes()));
        }

        parametersList = parametersList.AddParameters(this.TranslateDelegateToFunctionPointer(signature.ReturnType, this.GetReturnTypeCustomAttributes(methodDefinition)));

        return FunctionPointerType(callingConventionSyntax, parametersList);
    }

    private FunctionPointerParameterSyntax TranslateDelegateToFunctionPointer(TypeHandleInfo parameterTypeInfo, CustomAttributeHandleCollection? customAttributeHandles)
    {
        if (this.IsDelegateReference(parameterTypeInfo, out QualifiedTypeDefinition delegateTypeDef))
        {
            return FunctionPointerParameter(delegateTypeDef.Generator.FunctionPointer(delegateTypeDef.Definition));
        }

        return FunctionPointerParameter(parameterTypeInfo.ToTypeSyntax(this.functionPointerTypeSettings, GeneratingElement.FunctionPointer, customAttributeHandles?.QualifyWith(this)).GetUnmarshaledType());
    }

    // Generates an unsafe readonly struct that wraps the native function pointer for a delegate type definition.
    // Structure:
    // unsafe readonly struct <Name> { public readonly delegate* unmanaged<...> Value; public <Name>(delegate* unmanaged<...> value) => Value = value; public bool IsNull => Value is null; implicit conversions }
    private StructDeclarationSyntax DeclareTypeDefStructForNativeFunctionPointer(TypeDefinition typeDef, Context context)
    {
        // Delegates are generally managed types, except when using source generators when they never are.
        bool isManagedType = !this.UseSourceGenerators;
        string name = this.GetMangledIdentifier(this.Reader.GetString(typeDef.Name), context.AllowMarshaling, isManagedType);

        FunctionPointerTypeSyntax fpType = this.FunctionPointer(typeDef);

        // public readonly delegate* unmanaged<...> Value;
        FieldDeclarationSyntax valueField = FieldDeclaration(
            [TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.ReadOnlyKeyword)],
            VariableDeclaration(fpType.WithTrailingTrivia(TriviaList(Space)), [VariableDeclarator(Identifier("Value"))]));

        // public <Name>(delegate* unmanaged<...> value) => Value = value;
        ConstructorDeclarationSyntax ctor = ConstructorDeclaration(Identifier(name), [Parameter(fpType, Identifier("value"))])
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword))
            .WithExpressionBody(ArrowExpressionClause(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, IdentifierName("Value"), IdentifierName("value"))))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public static <Name> Null => default;
        PropertyDeclarationSyntax nullProperty = PropertyDeclaration(IdentifierName(name).WithTrailingTrivia(Space), Identifier("Null"))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword))
            .WithExpressionBody(ArrowExpressionClause(LiteralExpression(SyntaxKind.DefaultLiteralExpression)))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public bool IsNull => Value is null;
        PropertyDeclarationSyntax isNullProperty = PropertyDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), Identifier("IsNull"))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword))
            .WithExpressionBody(ArrowExpressionClause(IsPatternExpression(IdentifierName("Value"), ConstantPattern(LiteralExpression(SyntaxKind.NullLiteralExpression)))))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public static implicit operator <Name>(delegate* unmanaged<...> value) => new(value);
        ConversionOperatorDeclarationSyntax implicitFromPointer = ConversionOperatorDeclaration(Token(SyntaxKind.ImplicitKeyword), IdentifierName(name))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword))
            .AddParameterListParameters(Parameter(fpType, Identifier("value")))
            .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(IdentifierName(name), [Argument(IdentifierName("value"))])))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public static implicit operator delegate* unmanaged<...>(<Name> value) => value.Value;
        ConversionOperatorDeclarationSyntax implicitToPointer = ConversionOperatorDeclaration(Token(SyntaxKind.ImplicitKeyword), fpType)
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword))
            .AddParameterListParameters(Parameter(IdentifierName(name).WithTrailingTrivia(Space), Identifier("value")))
            .WithExpressionBody(ArrowExpressionClause(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("value"), IdentifierName("Value"))))
            .WithSemicolonToken(SemicolonWithLineFeed);

        StructDeclarationSyntax result = StructDeclaration(Identifier(name), [valueField, ctor, nullProperty, isNullProperty, implicitFromPointer, implicitToPointer])
            .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword), TokenWithSpace(SyntaxKind.ReadOnlyKeyword));

        return result;
    }
}
