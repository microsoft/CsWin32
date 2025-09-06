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
            result = result.AddAttributeLists(AttributeList().AddAttributes(UnmanagedFunctionPointer(callingConvention.Value)).WithCloseBracketToken(TokenWithLineFeed(SyntaxKind.CloseBracketToken)));
        }

        return result;
    }

    private MemberDeclarationSyntax DeclareUntypedDelegate(TypeDefinition typeDef)
    {
        IdentifierNameSyntax name = IdentifierName(this.Reader.GetString(typeDef.Name));
        IdentifierNameSyntax valueFieldName = IdentifierName("Value");

        // internal IntPtr Value;
        FieldDeclarationSyntax valueField = FieldDeclaration(VariableDeclaration(IntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space)))
            .AddVariables(VariableDeclarator(valueFieldName.Identifier))).AddModifiers(TokenWithSpace(this.Visibility));

        // internal T CreateDelegate<T>() => Marshal.GetDelegateForFunctionPointer<T>(this.Value);
        IdentifierNameSyntax typeParameter = IdentifierName("TDelegate");
        MemberAccessExpressionSyntax methodToCall = this.getDelegateForFunctionPointerGenericExists
            ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Marshal)), GenericName(nameof(Marshal.GetDelegateForFunctionPointer)).AddTypeArgumentListArguments(typeParameter))
            : MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Marshal)), IdentifierName(nameof(Marshal.GetDelegateForFunctionPointer)));
        ArgumentListSyntax arguments = ArgumentList().AddArguments(Argument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), valueFieldName)));
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
            .AddConstraintClauses(TypeParameterConstraintClause(typeParameter, SingletonSeparatedList<TypeParameterConstraintSyntax>(TypeConstraint(IdentifierName("Delegate")))))
            .WithExpressionBody(ArrowExpressionClause(bodyExpression))
            .AddModifiers(TokenWithSpace(this.Visibility))
            .WithSemicolonToken(SemicolonWithLineFeed);

        StructDeclarationSyntax typedefStruct = StructDeclaration(name.Identifier)
            .WithModifiers(TokenList(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.PartialKeyword)))
            .AddMembers(valueField)
            .AddMembers(this.CreateCommonTypeDefMembers(name, IntPtrTypeSyntax, valueFieldName).ToArray())
            .AddMembers(createDelegateMethod);
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
            FunctionPointerUnmanagedCallingConventionList(SingletonSeparatedList(ToUnmanagedCallingConventionSyntax(CallingConvention.StdCall))));

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
}
