// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    private static readonly IdentifierNameSyntax HRThrowOnFailureMethodName = IdentifierName("ThrowOnFailure");

    // [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static readonly AttributeListSyntax CcwEntrypointAttributes = AttributeList().AddAttributes(Attribute(IdentifierName("UnmanagedCallersOnly")).AddArgumentListArguments(
        AttributeArgument(ImplicitArrayCreationExpression(InitializerExpression(SyntaxKind.ArrayInitializerExpression, SingletonSeparatedList(TypeOfExpression(IdentifierName("CallConvStdcall"))))))
            .WithNameEquals(NameEquals(IdentifierName("CallConvs")))));

    private readonly HashSet<string> injectedPInvokeHelperMethodsToFriendlyOverloadsExtensions = new();

    private static Guid DecodeGuidFromAttribute(CustomAttribute guidAttribute)
    {
        CustomAttributeValue<TypeSyntax> args = guidAttribute.DecodeValue(CustomAttributeTypeProvider.Instance);
        return new Guid(
            (uint)args.FixedArguments[0].Value!,
            (ushort)args.FixedArguments[1].Value!,
            (ushort)args.FixedArguments[2].Value!,
            (byte)args.FixedArguments[3].Value!,
            (byte)args.FixedArguments[4].Value!,
            (byte)args.FixedArguments[5].Value!,
            (byte)args.FixedArguments[6].Value!,
            (byte)args.FixedArguments[7].Value!,
            (byte)args.FixedArguments[8].Value!,
            (byte)args.FixedArguments[9].Value!,
            (byte)args.FixedArguments[10].Value!);
    }

    private static bool IsHresult(TypeHandleInfo? typeHandleInfo) => typeHandleInfo is HandleTypeHandleInfo handleInfo && handleInfo.IsType("HRESULT");

    private static bool GenerateCcwFor(string interfaceName) => interfaceName is not ("IUnknown" or "IDispatch" or "IInspectable");

    private static bool GenerateCcwFor(MetadataReader reader, StringHandle typeName) => !(reader.StringComparer.Equals(typeName, "IUnknown") || reader.StringComparer.Equals(typeName, "IDispatch") || reader.StringComparer.Equals(typeName, "IInspectable"));

    /// <summary>
    /// Generates a type to represent a COM interface.
    /// </summary>
    /// <param name="typeDefHandle">The type definition handle of the interface.</param>
    /// <param name="context">The generation context.</param>
    /// <returns>The type declaration.</returns>
    /// <remarks>
    /// COM interfaces are represented as structs in order to maintain the "unmanaged type" trait
    /// so that all structs are blittable.
    /// </remarks>
    private TypeDeclarationSyntax? DeclareInterface(TypeDefinitionHandle typeDefHandle, Context context)
    {
        TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefHandle);
        var baseTypes = ImmutableStack.Create<QualifiedTypeDefinitionHandle>();
        (Generator Generator, InterfaceImplementationHandle Handle) baseInterfaceImplHandle = (this, typeDef.GetInterfaceImplementations().SingleOrDefault());
        while (!baseInterfaceImplHandle.Handle.IsNil)
        {
            InterfaceImplementation baseTypeImpl = baseInterfaceImplHandle.Generator.Reader.GetInterfaceImplementation(baseInterfaceImplHandle.Handle);
            if (!baseInterfaceImplHandle.Generator.TryGetTypeDefHandle((TypeReferenceHandle)baseTypeImpl.Interface, out QualifiedTypeDefinitionHandle baseTypeDefHandle))
            {
                throw new GenerationFailedException("Failed to find base type.");
            }

            baseTypes = baseTypes.Push(baseTypeDefHandle);
            TypeDefinition baseType = baseTypeDefHandle.Reader.GetTypeDefinition(baseTypeDefHandle.DefinitionHandle);
            baseInterfaceImplHandle = (baseTypeDefHandle.Generator, baseType.GetInterfaceImplementations().SingleOrDefault());
        }

        if (this.IsNonCOMInterface(typeDef))
        {
            // We cannot declare an interface that is not COM-compliant.
            return this.DeclareInterfaceAsStruct(typeDefHandle, baseTypes, context);
        }

        if (context.AllowMarshaling)
        {
            // Marshaling is allowed here, and generally. Just emit the interface.
            return this.DeclareInterfaceAsInterface(typeDef, baseTypes, context);
        }

        // Marshaling of this interface is not allowed here. Emit the struct.
        TypeDeclarationSyntax structDecl = this.DeclareInterfaceAsStruct(typeDefHandle, baseTypes, context);
        if (!this.options.AllowMarshaling)
        {
            // Marshaling isn't allowed over the entire compilation, so emit the interface nested under the struct so
            // it can be implemented and enable CCW scenarios.
            TypeDeclarationSyntax? ifaceDecl = this.DeclareInterfaceAsInterface(typeDef, baseTypes, context, interfaceAsSubtype: true);
            if (ifaceDecl is not null)
            {
                structDecl = structDecl.AddMembers(ifaceDecl);
            }
        }

        return structDecl;
    }

    private TypeDeclarationSyntax DeclareInterfaceAsStruct(TypeDefinitionHandle typeDefHandle, ImmutableStack<QualifiedTypeDefinitionHandle> baseTypes, Context context)
    {
        TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefHandle);
        string originalIfaceName = this.Reader.GetString(typeDef.Name);
        bool isManagedType = this.IsManagedType(typeDefHandle);
        IdentifierNameSyntax ifaceName = IdentifierName(this.GetMangledIdentifier(originalIfaceName, context.AllowMarshaling, isManagedType));
        IdentifierNameSyntax vtblFieldName = IdentifierName("lpVtbl");
        var members = new List<MemberDeclarationSyntax>();
        var vtblMembers = new List<MemberDeclarationSyntax>();
        TypeSyntaxSettings typeSettings = context.Filter(this.comSignatureTypeSettings);
        IdentifierNameSyntax pThisParameterName = IdentifierName("pThis");
        ExpressionSyntax pThis = ThisPointer(PointerType(ifaceName));
        ParameterSyntax? ccwThisParameter = this.canUseUnmanagedCallersOnlyAttribute && !this.options.AllowMarshaling && originalIfaceName != "IUnknown" && originalIfaceName != "IDispatch" && !this.IsNonCOMInterface(typeDef) ? Parameter(pThisParameterName.Identifier).WithType(PointerType(ifaceName).WithTrailingTrivia(Space)) : null;
        List<QualifiedMethodDefinitionHandle> ccwMethodsToSkip = new();
        List<MemberDeclarationSyntax> ccwEntrypointMethods = new();
        IdentifierNameSyntax vtblParamName = IdentifierName("vtable");
        BlockSyntax populateVTableBody = Block();
        IdentifierNameSyntax objectLocal = IdentifierName("__object");
        IdentifierNameSyntax hrLocal = IdentifierName("__hr");
        StatementSyntax returnSOK = ReturnStatement(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, HresultTypeSyntax, IdentifierName("S_OK")));

        this.MainGenerator.RequestInteropType("Windows.Win32.Foundation", "HRESULT", context);

        // It is imperative that we generate methods for all base interfaces as well, ahead of any implemented by *this* interface.
        var allMethods = new List<QualifiedMethodDefinitionHandle>();
        bool hasIUnknownMembers = originalIfaceName == "IUnknown";
        while (!baseTypes.IsEmpty)
        {
            QualifiedTypeDefinitionHandle qualifiedBaseType = baseTypes.Peek();
            baseTypes = baseTypes.Pop();
            TypeDefinition baseType = qualifiedBaseType.Generator.Reader.GetTypeDefinition(qualifiedBaseType.DefinitionHandle);
            IEnumerable<QualifiedMethodDefinitionHandle> methodsThisType = baseType.GetMethods().Select(m => new QualifiedMethodDefinitionHandle(qualifiedBaseType.Generator, m));
            allMethods.AddRange(methodsThisType);

            hasIUnknownMembers |= qualifiedBaseType.Reader.StringComparer.Equals(baseType.Name, "IUnknown");

            // We do *not* emit CCW methods for IUnknown, because those are provided by ComWrappers.
            if (ccwThisParameter is not null && !GenerateCcwFor(qualifiedBaseType.Reader, baseType.Name))
            {
                ccwMethodsToSkip.AddRange(methodsThisType);
            }
        }

        allMethods.AddRange(typeDef.GetMethods().Select(m => new QualifiedMethodDefinitionHandle(this, m)));
        int methodCounter = 0;
        HashSet<string> helperMethodsInStruct = new();
        HashSet<string> declaredProperties = new(StringComparer.Ordinal);
        declaredProperties.UnionWith(
            from method in allMethods
            group method by method.Generator into methodsByMetadata
            let methodDefs = methodsByMetadata.Select(qh => qh.Resolve())
            from property in methodsByMetadata.Key.GetDeclarableProperties(methodDefs, originalIfaceName, allowNonConsecutiveAccessors: true, context)
            select property);
        ISet<string>? ifaceDeclaredProperties = ccwThisParameter is not null ? this.GetDeclarableProperties(allMethods.Select(qh => qh.Resolve()), originalIfaceName, allowNonConsecutiveAccessors: false, context) : null;

        foreach (QualifiedMethodDefinitionHandle methodDefHandle in allMethods)
        {
            methodCounter++;
            QualifiedMethodDefinition methodDefinition = methodDefHandle.Resolve();
            string methodName = methodDefinition.Reader.GetString(methodDefinition.Method.Name);
            IdentifierNameSyntax innerMethodName = IdentifierName($"{methodName}_{methodCounter}");
            LiteralExpressionSyntax methodOffset = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(methodCounter - 1));

            MethodSignature<TypeHandleInfo> signature = methodDefinition.Method.DecodeSignature(SignatureHandleProvider.Instance, null);
            CustomAttributeHandleCollection? returnTypeAttributes = methodDefinition.Generator.GetReturnTypeCustomAttributes(methodDefinition.Method);
            TypeSyntax returnType = signature.ReturnType.ToTypeSyntax(typeSettings, GeneratingElement.InterfaceAsStructMember, returnTypeAttributes).Type;
            TypeSyntax returnTypePreserveSig = returnType;

            ParameterListSyntax parameterList = methodDefinition.Generator.CreateParameterList(methodDefinition.Method, signature, typeSettings with { Generator = methodDefinition.Generator }, GeneratingElement.InterfaceAsStructMember);
            ParameterListSyntax parameterListPreserveSig = parameterList; // preserve a copy that has no mutations.
            bool requiresMarshaling = parameterList.Parameters.Any(p => p.AttributeLists.SelectMany(al => al.Attributes).Any(a => a.Name is IdentifierNameSyntax { Identifier.ValueText: "MarshalAs" }) || p.Modifiers.Any(SyntaxKind.RefKeyword) || p.Modifiers.Any(SyntaxKind.OutKeyword) || p.Modifiers.Any(SyntaxKind.InKeyword));
            FunctionPointerParameterListSyntax funcPtrParameters = FunctionPointerParameterList()
                .AddParameters(FunctionPointerParameter(PointerType(ifaceName)))
                .AddParameters(parameterList.Parameters.Select(p => FunctionPointerParameter(p.Type!).WithModifiers(p.Modifiers)).ToArray())
                .AddParameters(FunctionPointerParameter(returnType));

            TypeSyntax unmanagedDelegateType = FunctionPointerType().WithCallingConvention(
                FunctionPointerCallingConvention(TokenWithSpace(SyntaxKind.UnmanagedKeyword))
                    .WithUnmanagedCallingConventionList(FunctionPointerUnmanagedCallingConventionList(
                        SingletonSeparatedList(FunctionPointerUnmanagedCallingConvention(Identifier("Stdcall"))))))
                .WithParameterList(funcPtrParameters);
            FieldDeclarationSyntax vtblFunctionPtr = FieldDeclaration(
                VariableDeclaration(unmanagedDelegateType)
                .WithVariables(SingletonSeparatedList(VariableDeclarator(innerMethodName.Identifier))))
                .WithModifiers(TokenList(TokenWithSpace(SyntaxKind.InternalKeyword)));
            vtblMembers.Add(vtblFunctionPtr);

            // Build up an unmanaged delegate cast directly from the vtbl pointer and invoke it.
            // By doing this, we make the emitted code more trimmable by not referencing the full virtual method table and its full set of types
            // when the app may only invoke a subset of the methods.
            //// ((delegate *unmanaged [Stdcall]<IPersist*,global::System.Guid* ,winmdroot.Foundation.HRESULT>)lpVtbl[3])(pThis, pClassID)
            ExpressionSyntax vtblIndexingExpression = ParenthesizedExpression(
                CastExpression(unmanagedDelegateType, ElementAccessExpression(vtblFieldName).AddArgumentListArguments(Argument(methodOffset))));
            InvocationExpressionSyntax vtblInvocation = InvocationExpression(vtblIndexingExpression)
                .WithArgumentList(FixTrivia(ArgumentList()
                    .AddArguments(Argument(pThis))
                    .AddArguments(parameterList.Parameters.Select(p => Argument(IdentifierName(p.Identifier.ValueText)).WithRefKindKeyword(p.Modifiers.Count > 0 ? p.Modifiers[0] : default)).ToArray())));

            MemberDeclarationSyntax? propertyOrMethod;
            MethodDeclarationSyntax? methodDeclaration = null;

            // We can declare this method as a property accessor if it represents a property.
            // We must also confirm that the property type is the same in both cases, because sometimes they aren't (e.g. IUIAutomationProxyFactoryEntry.ClassName).
            if (methodDefinition.Generator.TryGetPropertyAccessorInfo(methodDefinition.Method, originalIfaceName, context, out IdentifierNameSyntax? propertyName, out SyntaxKind? accessorKind, out TypeSyntax? propertyType) &&
                declaredProperties.Contains(propertyName.Identifier.ValueText))
            {
                StatementSyntax ThrowOnHRFailure(ExpressionSyntax hrExpression) => ExpressionStatement(InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, hrExpression, HRThrowOnFailureMethodName),
                    ArgumentList()));

                BlockSyntax? body;
                switch (accessorKind)
                {
                    case SyntaxKind.GetAccessorDeclaration:
                        // PropertyType __result;
                        IdentifierNameSyntax resultLocal = IdentifierName("__result");
                        LocalDeclarationStatementSyntax resultLocalDeclaration = LocalDeclarationStatement(VariableDeclaration(propertyType).AddVariables(VariableDeclarator(resultLocal.Identifier)));

                        // vtblInvoke(pThis, &__result).ThrowOnFailure();
                        // vtblInvoke(pThis, out __result).ThrowOnFailure();
                        ArgumentSyntax resultArgument = funcPtrParameters.Parameters[1].Modifiers.Any(SyntaxKind.OutKeyword)
                            ? Argument(resultLocal).WithRefKindKeyword(Token(SyntaxKind.OutKeyword))
                            : Argument(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, resultLocal));
                        StatementSyntax vtblInvocationStatement = ThrowOnHRFailure(vtblInvocation.WithArgumentList(ArgumentList().AddArguments(Argument(pThis), resultArgument)));

                        // return __result;
                        StatementSyntax returnStatement = ReturnStatement(resultLocal);

                        body = Block().AddStatements(
                            resultLocalDeclaration,
                            vtblInvocationStatement,
                            returnStatement);
                        break;
                    case SyntaxKind.SetAccessorDeclaration:
                        // vtblInvoke(pThis, value).ThrowOnFailure();
                        vtblInvocationStatement = ThrowOnHRFailure(vtblInvocation.WithArgumentList(ArgumentList().AddArguments(Argument(pThis), Argument(IdentifierName("value")))));
                        body = Block().AddStatements(vtblInvocationStatement);
                        break;
                    default:
                        throw new NotSupportedException("Unsupported accessor kind: " + accessorKind);
                }

                AccessorDeclarationSyntax accessor = AccessorDeclaration(accessorKind.Value, body);

                int priorPropertyDeclarationIndex = members.FindIndex(m => m is PropertyDeclarationSyntax prop && prop.Identifier.ValueText == propertyName.Identifier.ValueText);
                if (priorPropertyDeclarationIndex >= 0)
                {
                    // Add the accessor to the existing property declaration.
                    PropertyDeclarationSyntax priorDeclaration = (PropertyDeclarationSyntax)members[priorPropertyDeclarationIndex];
                    members[priorPropertyDeclarationIndex] = priorDeclaration.WithAccessorList(priorDeclaration.AccessorList!.AddAccessors(accessor));
                    propertyOrMethod = null;
                }
                else
                {
                    PropertyDeclarationSyntax propertyDeclaration = PropertyDeclaration(propertyType.WithTrailingTrivia(Space), propertyName.Identifier.WithTrailingTrivia(LineFeed));

                    propertyDeclaration = propertyDeclaration
                        .WithAccessorList(AccessorList().AddAccessors(accessor))
                        .AddModifiers(Token(this.Visibility));

                    if (propertyDeclaration.Type is PointerTypeSyntax)
                    {
                        propertyDeclaration = propertyDeclaration.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
                    }

                    propertyOrMethod = propertyDeclaration;
                }
            }
            else
            {
                BlockSyntax body;
                bool preserveSig = methodDefinition.Generator.UsePreserveSigForComMethod(methodDefinition.Method, signature, ifaceName.Identifier.ValueText, methodName);
                if (preserveSig)
                {
                    // return ...
                    body = Block().AddStatements(
                        IsVoid(returnType)
                            ? ExpressionStatement(vtblInvocation)
                            : ReturnStatement(vtblInvocation));
                }
                else
                {
                    // hrReturningInvocation().ThrowOnFailure();
                    StatementSyntax InvokeVtblAndThrow() => ExpressionStatement(InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, vtblInvocation, HRThrowOnFailureMethodName),
                        ArgumentList()));

                    ParameterSyntax? lastParameter = parameterList.Parameters.Count > 0 ? parameterList.Parameters[parameterList.Parameters.Count - 1] : null;
                    if (lastParameter?.HasAnnotation(IsRetValAnnotation) is true)
                    {
                        // Move the retval parameter to the return value position.
                        parameterList = parameterList.WithParameters(parameterList.Parameters.RemoveAt(parameterList.Parameters.Count - 1));
                        returnType = lastParameter.Modifiers.Any(SyntaxKind.OutKeyword) ? lastParameter.Type! : ((PointerTypeSyntax)lastParameter.Type!).ElementType;

                        // Guid __retVal = default(Guid);
                        IdentifierNameSyntax retValLocalName = IdentifierName("__retVal");
                        LocalDeclarationStatementSyntax localRetValDecl = LocalDeclarationStatement(VariableDeclaration(returnType).AddVariables(
                            VariableDeclarator(retValLocalName.Identifier).WithInitializer(EqualsValueClause(DefaultExpression(returnType)))));

                        // Modify the vtbl invocation's last argument to point to our own local variable.
                        ArgumentSyntax lastArgument = lastParameter.Modifiers.Any(SyntaxKind.OutKeyword)
                            ? Argument(retValLocalName).WithRefKindKeyword(TokenWithSpace(SyntaxKind.OutKeyword))
                            : Argument(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, retValLocalName));
                        vtblInvocation = vtblInvocation.WithArgumentList(
                            vtblInvocation.ArgumentList.WithArguments(vtblInvocation.ArgumentList.Arguments.Replace(vtblInvocation.ArgumentList.Arguments.Last(), lastArgument)));

                        // return __retVal;
                        ReturnStatementSyntax returnStatement = ReturnStatement(retValLocalName);

                        body = Block().AddStatements(localRetValDecl, InvokeVtblAndThrow(), returnStatement);
                    }
                    else
                    {
                        // Remove the return type
                        returnType = PredefinedType(Token(SyntaxKind.VoidKeyword));

                        body = Block().AddStatements(InvokeVtblAndThrow());
                    }
                }

                methodDeclaration = MethodDeclaration(
                    List<AttributeListSyntax>(),
                    modifiers: TokenList(TokenWithSpace(SyntaxKind.PublicKeyword)), // always use public so struct can implement the COM interface
                    returnType.WithTrailingTrivia(TriviaList(Space)),
                    explicitInterfaceSpecifier: null,
                    SafeIdentifier(methodName),
                    null,
                    parameterList,
                    List<TypeParameterConstraintClauseSyntax>(),
                    body: body,
                    semicolonToken: default);

                if (methodName is nameof(object.GetType) or nameof(object.ToString) && parameterList.Parameters.Count == 0)
                {
                    methodDeclaration = methodDeclaration.AddModifiers(TokenWithSpace(SyntaxKind.NewKeyword));
                }

                if (methodDeclaration.ReturnType is PointerTypeSyntax || methodDeclaration.ParameterList.Parameters.Any(p => p.Type is PointerTypeSyntax))
                {
                    methodDeclaration = methodDeclaration.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
                }

                propertyOrMethod = methodDeclaration;

                members.AddRange(methodDefinition.Generator.DeclareFriendlyOverloads(methodDefinition.Method, methodDeclaration, IdentifierName(ifaceName.Identifier.ValueText), FriendlyOverloadOf.StructMethod, helperMethodsInStruct));
            }

            if (ccwThisParameter is not null && !ccwMethodsToSkip.Contains(methodDefHandle))
            {
                if (this.TryGetPropertyAccessorInfo(methodDefinition.Method, originalIfaceName, context, out propertyName, out accessorKind, out propertyType) &&
                    ifaceDeclaredProperties!.Contains(propertyName.Identifier.ValueText))
                {
                    switch (accessorKind)
                    {
                        case SyntaxKind.GetAccessorDeclaration:
                            //// *inputArg = @object.Property;
                            StatementSyntax propertyGet = ExpressionStatement(AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                PrefixUnaryExpression(SyntaxKind.PointerIndirectionExpression, IdentifierName(parameterListPreserveSig.Parameters.Last().Identifier.ValueText)),
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, objectLocal, propertyName)));
                            this.TryGenerateConstantOrThrow("S_OK");
                            AddCcwThunk(propertyGet, returnSOK);
                            break;
                        case SyntaxKind.SetAccessorDeclaration:
                            //// @object.Property = inputArg;
                            StatementSyntax propertySet = ExpressionStatement(AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, objectLocal, propertyName),
                                IdentifierName(parameterListPreserveSig.Parameters.Last().Identifier.ValueText)));
                            this.TryGenerateConstantOrThrow("S_OK");
                            AddCcwThunk(propertySet, returnSOK);
                            break;
                        default:
                            throw new NotSupportedException("Unsupported accessor kind: " + accessorKind);
                    }
                }
                else
                {
                    // Prepare the args for the thunk call. The Interface we thunk into *always* uses PreserveSig, which is super convenient for us.
                    ArgumentListSyntax args = ArgumentList().AddArguments(parameterListPreserveSig.Parameters.Select(p => Argument(IdentifierName(p.Identifier.ValueText))).ToArray());

                    // @object!.SomeMethod(args)
                    InvocationExpressionSyntax thunkInvoke = InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, objectLocal, SafeIdentifierName(methodName)),
                        args);

                    StatementSyntax returnManagedMethodInvocation = returnTypePreserveSig is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword }
                        ? ExpressionStatement(thunkInvoke)
                        : ReturnStatement(thunkInvoke);

                    AddCcwThunk(returnManagedMethodInvocation);
                }
            }

            void AddCcwThunk(params StatementSyntax[] thunkInvokeAndReturn)
            {
                if (ccwThisParameter is null || ccwMethodsToSkip.Contains(methodDefHandle))
                {
                    return;
                }

                if (requiresMarshaling)
                {
                    // Oops. This method requires marshaling, which isn't supported in a native-callable function.
                    // Abandon all efforts to add CCW support to this interface.
                    ccwThisParameter = null;
                    foreach (MethodDeclarationSyntax ccwEntrypointMethod in ccwEntrypointMethods)
                    {
                        members.Remove(ccwEntrypointMethod);
                    }

                    ccwEntrypointMethods.Clear();
                    return;
                }

                this.RequestComHelpers(context);
                bool hrReturnType = returnTypePreserveSig is QualifiedNameSyntax { Right.Identifier.ValueText: "HRESULT" };

                //// HRESULT hr = ComHelpers.UnwrapCCW(@this, out Interface? @object);
                LocalDeclarationStatementSyntax hrDecl = LocalDeclarationStatement(VariableDeclaration(HresultTypeSyntax).AddVariables(
                    VariableDeclarator(hrLocal.Identifier).WithInitializer(EqualsValueClause(
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("ComHelpers"), IdentifierName("UnwrapCCW")),
                            ArgumentList().AddArguments(
                                Argument(pThisParameterName),
                                Argument(DeclarationExpression(NestedCOMInterfaceName.WithTrailingTrivia(Space), SingleVariableDesignation(objectLocal.Identifier))).WithRefKindKeyword(Token(SyntaxKind.OutKeyword))))))));

                StatementSyntax ifNullReturnStatement = hrReturnType
                    //// if (hr.Failed) return hr;
                    ? IfStatement(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, hrLocal, IdentifierName("Failed")),
                        Block().AddStatements(ReturnStatement(hrLocal)))
                    //// hr.ThrowOnFailure();
                    : ExpressionStatement(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, hrLocal, HRThrowOnFailureMethodName)));

                IdentifierNameSyntax exLocal = IdentifierName("ex");
                BlockSyntax catchBlock = Block();
                if (hrReturnType)
                {
                    //// return (HRESULT)ex.HResult;
                    catchBlock = catchBlock.AddStatements(ReturnStatement(CastExpression(HresultTypeSyntax, MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, exLocal, IdentifierName(nameof(Exception.HResult))))));
                }
                else
                {
                    //// Environment.FailFast("COM object threw an exception from a non-HRESULT returning method.", ex);
                    //// throw;
                    catchBlock = catchBlock.AddStatements(
                        ExpressionStatement(InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ParseName("global::System.Environment"), IdentifierName(nameof(Environment.FailFast))),
                            ArgumentList().AddArguments(
                                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("COM object threw an exception from a non-HRESULT returning method."))),
                                Argument(exLocal)))),
                        ThrowStatement());
                }

                //// catch (Exception ex) {
                CatchClauseSyntax catchClause = CatchClause(CatchDeclaration(IdentifierName(nameof(Exception)).WithTrailingTrivia(Space), exLocal.Identifier), null, catchBlock);

                BlockSyntax tryBlock = Block().AddStatements(
                    hrDecl,
                    ifNullReturnStatement).AddStatements(thunkInvokeAndReturn);

                //// try { ... } catch { ... }
                BlockSyntax ccwBody = Block().AddStatements(TryStatement(tryBlock, new SyntaxList<CatchClauseSyntax>(catchClause), null));

                //// [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
                //// private static HRESULT Clone(IEnumEventObject* @this, IEnumEventObject** ppInterface)
                MethodDeclarationSyntax ccwMethod = MethodDeclaration(
                    new SyntaxList<AttributeListSyntax>(CcwEntrypointAttributes),
                    TokenList(TokenWithSpace(SyntaxKind.PrivateKeyword), Token(SyntaxKind.StaticKeyword)),
                    returnTypePreserveSig,
                    explicitInterfaceSpecifier: null,
                    SafeIdentifier(methodName),
                    typeParameterList: null,
                    ParameterList().WithParameters(parameterListPreserveSig.Parameters.Insert(0, ccwThisParameter)),
                    constraintClauses: default,
                    ccwBody,
                    semicolonToken: default);
                members.Add(ccwMethod);
                ccwEntrypointMethods.Add(ccwMethod);

                populateVTableBody = populateVTableBody.AddStatements(
                    ExpressionStatement(AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        MemberAccessExpression(SyntaxKind.PointerMemberAccessExpression, vtblParamName, innerMethodName),
                        PrefixUnaryExpression(SyntaxKind.AddressOfExpression, SafeIdentifierName(methodName)))));
            }

            if (propertyOrMethod is not null)
            {
                // Add documentation if we can find it.
                propertyOrMethod = this.AddApiDocumentation($"{ifaceName}.{methodName}", propertyOrMethod);
                members.Add(propertyOrMethod);
            }
        }

        static ExpressionSyntax ThisPointer(PointerTypeSyntax? typedPointer = null)
        {
            // (type*)Unsafe.AsPointer(ref this)
            InvocationExpressionSyntax invocation = InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Unsafe)), IdentifierName(nameof(Unsafe.AsPointer))),
                ArgumentList().AddArguments(Argument(RefExpression(ThisExpression()))));
            return typedPointer is not null ? CastExpression(typedPointer, invocation) : invocation;
        }

        // Add helper methods when appropriate.
        if (hasIUnknownMembers && this.Options.FriendlyOverloads.Enabled)
        {
            members.AddRange(this.ExtractMembersFromTemplate("IUnknownHelperMethods"));
        }

        // We expose the vtbl struct to support CCWs.
        IdentifierNameSyntax vtblStructName = IdentifierName("Vtbl");
        StructDeclarationSyntax? vtblStruct = StructDeclaration(Identifier("Vtbl")).WithTrailingTrivia(Space)
            .AddMembers(vtblMembers.ToArray())
            .AddModifiers(TokenWithSpace(this.Visibility));
        members.Add(vtblStruct);

        if (ccwThisParameter is not null)
        {
            // PopulateVTable must be public in order to (implicitly) implement the IVTable<TComInterface, TVTable> interface.
            // public static void PopulateVTable(Vtbl* vtable)
            MethodDeclarationSyntax populateVtblMethodDecl = MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier("PopulateVTable"))
                .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
                .AddParameterListParameters(Parameter(vtblParamName.Identifier).WithType(PointerType(vtblStructName).WithTrailingTrivia(Space)))
                .WithBody(populateVTableBody);
            members.Add(populateVtblMethodDecl);

            if (populateVTableBody.Statements.Count != allMethods.Count - ccwMethodsToSkip.Count)
            {
                // We failed to initialize all the necessary vtbl entries.
                throw new GenerationFailedException("Internal error while generating CCW vtbl initializer.");
            }
        }

        // private void** lpVtbl; // Vtbl* (but we avoid strong typing to enable trimming the entire vtbl struct away)
        members.Add(FieldDeclaration(VariableDeclaration(PointerType(PointerType(PredefinedType(Token(SyntaxKind.VoidKeyword))))).AddVariables(VariableDeclarator(vtblFieldName.Identifier))).AddModifiers(TokenWithSpace(SyntaxKind.PrivateKeyword)));

        BaseListSyntax baseList = BaseList(SeparatedList<BaseTypeSyntax>());

        CustomAttribute? guidAttribute = this.FindGuidAttribute(typeDef.GetCustomAttributes());
        var staticMembers = this.DeclareStaticCOMInterfaceMembers(originalIfaceName, ifaceName, ccwThisParameter is not null, guidAttribute, context);
        members.AddRange(staticMembers.Members);
        baseList = baseList.AddTypes(staticMembers.BaseTypes.ToArray());

        StructDeclarationSyntax iface = StructDeclaration(ifaceName.Identifier)
            .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword), TokenWithSpace(SyntaxKind.PartialKeyword))
            .AddMembers(members.ToArray());

        if (baseList.Types.Count > 0)
        {
            iface = iface.WithBaseList(baseList);
        }

        if (guidAttribute.HasValue)
        {
            iface = iface.AddAttributeLists(AttributeList().AddAttributes(GUID(DecodeGuidFromAttribute(guidAttribute.Value))));
        }

        if (this.GetSupportedOSPlatformAttribute(typeDef.GetCustomAttributes()) is AttributeSyntax supportedOSPlatformAttribute)
        {
            iface = iface.AddAttributeLists(AttributeList().AddAttributes(supportedOSPlatformAttribute));
        }

        return iface;
    }

    private TypeDeclarationSyntax? DeclareInterfaceAsInterface(TypeDefinition typeDef, ImmutableStack<QualifiedTypeDefinitionHandle> baseTypes, Context context, bool interfaceAsSubtype = false)
    {
        if (this.Reader.StringComparer.Equals(typeDef.Name, "IUnknown") || this.Reader.StringComparer.Equals(typeDef.Name, "IDispatch"))
        {
            // We do not generate interfaces for these COM base types.
            return null;
        }

        string actualIfaceName = this.Reader.GetString(typeDef.Name);
        IdentifierNameSyntax ifaceName = interfaceAsSubtype ? NestedCOMInterfaceName : IdentifierName(actualIfaceName);
        TypeSyntaxSettings typeSettings = this.comSignatureTypeSettings;

        // It is imperative that we generate methods for all base interfaces as well, ahead of any implemented by *this* interface.
        var allMethods = new List<QualifiedMethodDefinitionHandle>();
        bool foundIUnknown = false;
        bool foundIDispatch = false;
        bool foundIInspectable = false;
        var baseTypeSyntaxList = new List<BaseTypeSyntax>();
        while (!baseTypes.IsEmpty)
        {
            QualifiedTypeDefinitionHandle baseTypeHandle = baseTypes.Peek();
            baseTypes = baseTypes.Pop();
            QualifiedTypeDefinition baseType = baseTypeHandle.Resolve();
            if (!foundIUnknown)
            {
                if (!baseTypeHandle.Reader.StringComparer.Equals(baseType.Definition.Name, "IUnknown"))
                {
                    throw new NotSupportedException("Unsupported base COM interface type: " + baseTypeHandle.Reader.GetString(baseType.Definition.Name));
                }

                foundIUnknown = true;
            }
            else
            {
                if (baseTypeHandle.Reader.StringComparer.Equals(baseType.Definition.Name, "IDispatch"))
                {
                    foundIDispatch = true;
                }
                else if (baseTypeHandle.Reader.StringComparer.Equals(baseType.Definition.Name, "IInspectable"))
                {
                    foundIInspectable = true;
                }
                else
                {
                    baseTypeHandle.Generator.RequestInteropType(baseTypeHandle.DefinitionHandle, context);
                    TypeSyntax baseTypeSyntax = new HandleTypeHandleInfo(baseTypeHandle.Reader, baseTypeHandle.DefinitionHandle).ToTypeSyntax(this.comSignatureTypeSettings, GeneratingElement.InterfaceMember, null).Type;
                    if (interfaceAsSubtype)
                    {
                        baseTypeSyntax = QualifiedName(
                            baseTypeSyntax is PointerTypeSyntax baseTypePtr ? (NameSyntax)baseTypePtr.ElementType : (NameSyntax)baseTypeSyntax,
                            NestedCOMInterfaceName);
                    }

                    baseTypeSyntaxList.Add(SimpleBaseType(baseTypeSyntax));
                    allMethods.AddRange(baseType.Definition.GetMethods().Select(methodHandle => new QualifiedMethodDefinitionHandle(baseType.Generator, methodHandle)));
                }
            }
        }

        int inheritedMethods = allMethods.Count;
        allMethods.AddRange(typeDef.GetMethods().Select(methodHandle => new QualifiedMethodDefinitionHandle(this, methodHandle)));

        AttributeSyntax ifaceType = InterfaceType(
            foundIInspectable ? ComInterfaceType.InterfaceIsIInspectable :
            foundIDispatch ? (allMethods.Count == 0 ? ComInterfaceType.InterfaceIsIDispatch : ComInterfaceType.InterfaceIsDual) :
            foundIUnknown ? ComInterfaceType.InterfaceIsIUnknown :
            throw new NotSupportedException("No COM interface base type found."));

        var members = new List<MemberDeclarationSyntax>();
        var friendlyOverloads = new List<MethodDeclarationSyntax>();
        ISet<string> declaredProperties = this.GetDeclarableProperties(allMethods.Select(method => method.Resolve()), actualIfaceName, allowNonConsecutiveAccessors: false, context);

        foreach (QualifiedMethodDefinitionHandle methodDefHandle in allMethods)
        {
            QualifiedMethodDefinition methodDefinition = methodDefHandle.Resolve();
            string methodName = methodDefinition.Reader.GetString(methodDefinition.Method.Name);
            inheritedMethods--;
            try
            {
                MemberDeclarationSyntax propertyOrMethod;
                MethodDeclarationSyntax? methodDeclaration = null;

                // Consider whether we should declare this as a property.
                // Even if it could be represented as a property accessor, we cannot do so if a property by the same name was already declared in anything other than the previous row.
                // Adding an accessor to a property later than the very next row would screw up the virtual method table ordering.
                // We must also confirm that the property type is the same in both cases, because sometimes they aren't (e.g. IUIAutomationProxyFactoryEntry.ClassName).
                if (methodDefinition.Generator.TryGetPropertyAccessorInfo(methodDefinition.Method, actualIfaceName, context, out IdentifierNameSyntax? propertyName, out SyntaxKind? accessorKind, out TypeSyntax? propertyType) && declaredProperties.Contains(propertyName.Identifier.ValueText))
                {
                    AccessorDeclarationSyntax accessor = AccessorDeclaration(accessorKind.Value).WithSemicolonToken(Semicolon);

                    if (members.Count > 0 && members[members.Count - 1] is PropertyDeclarationSyntax lastProperty && lastProperty.Identifier.ValueText == propertyName.Identifier.ValueText)
                    {
                        // Add the accessor to the existing property declaration.
                        members[members.Count - 1] = lastProperty.WithAccessorList(lastProperty.AccessorList!.AddAccessors(accessor));
                        continue;
                    }
                    else
                    {
                        PropertyDeclarationSyntax propertyDeclaration = PropertyDeclaration(propertyType.WithTrailingTrivia(Space), propertyName.Identifier.WithTrailingTrivia(LineFeed));

                        propertyDeclaration = propertyDeclaration.WithAccessorList(AccessorList().AddAccessors(accessor));

                        if (propertyDeclaration.Type is PointerTypeSyntax)
                        {
                            propertyDeclaration = propertyDeclaration.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
                        }

                        propertyOrMethod = propertyDeclaration;
                    }
                }
                else
                {
                    MethodSignature<TypeHandleInfo> signature = methodDefinition.Method.DecodeSignature(SignatureHandleProvider.Instance, null);

                    CustomAttributeHandleCollection? returnTypeAttributes = this.GetReturnTypeCustomAttributes(methodDefinition.Method);
                    TypeSyntaxAndMarshaling returnTypeDetails = signature.ReturnType.ToTypeSyntax(typeSettings, GeneratingElement.InterfaceMember, returnTypeAttributes);
                    TypeSyntax returnType = returnTypeDetails.Type;
                    AttributeSyntax? returnsAttribute = MarshalAs(returnTypeDetails.MarshalAsAttribute, returnTypeDetails.NativeArrayInfo);

                    ParameterListSyntax? parameterList = this.CreateParameterList(methodDefinition.Method, signature, this.comSignatureTypeSettings, GeneratingElement.InterfaceMember);

                    bool preserveSig = interfaceAsSubtype || this.UsePreserveSigForComMethod(methodDefinition.Method, signature, actualIfaceName, methodName);
                    if (!preserveSig)
                    {
                        ParameterSyntax? lastParameter = parameterList.Parameters.Count > 0 ? parameterList.Parameters[parameterList.Parameters.Count - 1] : null;
                        if (lastParameter?.HasAnnotation(IsRetValAnnotation) is true)
                        {
                            // Move the retval parameter to the return value position.
                            parameterList = parameterList.WithParameters(parameterList.Parameters.RemoveAt(parameterList.Parameters.Count - 1));
                            returnType = lastParameter.Modifiers.Any(SyntaxKind.OutKeyword) ? lastParameter.Type! : ((PointerTypeSyntax)lastParameter.Type!).ElementType;
                            returnsAttribute = lastParameter.DescendantNodes().OfType<AttributeSyntax>().FirstOrDefault(att => att.Name.ToString() == "MarshalAs");
                        }
                        else
                        {
                            // Remove the return type
                            returnType = PredefinedType(Token(SyntaxKind.VoidKeyword));
                        }
                    }

                    methodDeclaration = MethodDeclaration(returnType.WithTrailingTrivia(TriviaList(Space)), SafeIdentifier(methodName))
                        .WithParameterList(FixTrivia(parameterList))
                        .WithSemicolonToken(SemicolonWithLineFeed);
                    if (returnsAttribute is object)
                    {
                        methodDeclaration = methodDeclaration.AddAttributeLists(
                            AttributeList().WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.ReturnKeyword))).AddAttributes(returnsAttribute));
                    }

                    if (preserveSig)
                    {
                        methodDeclaration = methodDeclaration.AddAttributeLists(AttributeList().AddAttributes(PreserveSigAttributeSyntax));
                    }

                    if (methodDeclaration.ReturnType is PointerTypeSyntax || methodDeclaration.ParameterList.Parameters.Any(p => p.Type is PointerTypeSyntax))
                    {
                        methodDeclaration = methodDeclaration.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
                    }

                    propertyOrMethod = methodDeclaration;
                }

                if (inheritedMethods >= 0)
                {
                    propertyOrMethod = propertyOrMethod.AddModifiers(TokenWithSpace(SyntaxKind.NewKeyword));
                }

                // Add documentation if we can find it.
                propertyOrMethod = this.AddApiDocumentation($"{ifaceName}.{methodName}", propertyOrMethod);
                members.Add(propertyOrMethod);

                if (methodDeclaration is not null)
                {
                    NameSyntax declaringTypeName = HandleTypeHandleInfo.GetNestingQualifiedName(this, this.Reader, typeDef, hasUnmanagedSuffix: false, isInterfaceNestedInStruct: interfaceAsSubtype);
                    friendlyOverloads.AddRange(
                        this.DeclareFriendlyOverloads(methodDefinition.Method, methodDeclaration, declaringTypeName, FriendlyOverloadOf.InterfaceMethod, this.injectedPInvokeHelperMethodsToFriendlyOverloadsExtensions));
                }
            }
            catch (Exception ex)
            {
                throw new GenerationFailedException($"Failed while generating the method: {methodName}", ex);
            }
        }

        CustomAttribute? guidAttribute = this.FindGuidAttribute(typeDef.GetCustomAttributes());

        InterfaceDeclarationSyntax ifaceDeclaration = InterfaceDeclaration(ifaceName.Identifier)
            .WithKeyword(TokenWithSpace(SyntaxKind.InterfaceKeyword))
            .AddModifiers(TokenWithSpace(this.Visibility))
            .AddMembers(members.ToArray());

        if (guidAttribute.HasValue)
        {
            ifaceDeclaration = ifaceDeclaration.AddAttributeLists(AttributeList().AddAttributes(GUID(DecodeGuidFromAttribute(guidAttribute.Value)), ifaceType, ComImportAttributeSyntax));
        }

        if (baseTypeSyntaxList.Count > 0)
        {
            ifaceDeclaration = ifaceDeclaration
                .WithBaseList(BaseList(SeparatedList(baseTypeSyntaxList)));
        }

        if (this.generateSupportedOSPlatformAttributesOnInterfaces && this.GetSupportedOSPlatformAttribute(typeDef.GetCustomAttributes()) is AttributeSyntax supportedOSPlatformAttribute)
        {
            ifaceDeclaration = ifaceDeclaration.AddAttributeLists(AttributeList().AddAttributes(supportedOSPlatformAttribute));
        }

        // Only add overloads to instance collections after everything else is done,
        // so we don't leave extension methods behind if we fail to generate the target interface.
        if (friendlyOverloads.Count > 0)
        {
            string ns = this.Reader.GetString(typeDef.Namespace);
            if (this.TryStripCommonNamespace(ns, out string? strippedNamespace))
            {
                ns = strippedNamespace;
            }

            ClassDeclarationSyntax friendlyOverloadClass = ClassDeclaration(Identifier($"{ns.Replace('.', '_')}_{actualIfaceName}_Extensions"))
                .WithMembers(List<MemberDeclarationSyntax>(friendlyOverloads))
                .WithModifiers(TokenList(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.PartialKeyword)))
                .AddAttributeLists(AttributeList().AddAttributes(GeneratedCodeAttribute));
            this.volatileCode.AddComInterfaceExtension(friendlyOverloadClass);
        }

        return ifaceDeclaration;
    }

    private unsafe (IReadOnlyList<MemberDeclarationSyntax> Members, IReadOnlyList<BaseTypeSyntax> BaseTypes) DeclareStaticCOMInterfaceMembers(
        string originalIfaceName,
        IdentifierNameSyntax ifaceName,
        bool populateVtblDeclared,
        CustomAttribute? guidAttribute,
        Context context)
    {
        List<MemberDeclarationSyntax> members = new();
        List<BaseTypeSyntax> baseTypes = new();

        // IVTable<ComStructType, ComStructType.Vtbl>
        // Static interface members require C# 11 and .NET 7 at minimum.
        if (populateVtblDeclared && this.IsFeatureAvailable(Feature.InterfaceStaticMembers) && !context.AllowMarshaling && GenerateCcwFor(originalIfaceName))
        {
            this.RequestComHelpers(context);
            baseTypes.Add(SimpleBaseType(GenericName("IVTable").AddTypeArgumentListArguments(
                ifaceName,
                QualifiedName(ifaceName, IdentifierName("Vtbl")))));
        }

        // IComIID
        if (guidAttribute.HasValue)
        {
            Guid guidAttributeValue = DecodeGuidFromAttribute(guidAttribute.Value);

            // internal static readonly Guid IID_Guid = new Guid(0x1234, ...);
            IdentifierNameSyntax iidGuidFieldName = IdentifierName("IID_Guid");
            TypeSyntax guidTypeSyntax = IdentifierName(nameof(Guid));
            members.Add(FieldDeclaration(
                VariableDeclaration(guidTypeSyntax)
                .AddVariables(VariableDeclarator(iidGuidFieldName.Identifier).WithInitializer(EqualsValueClause(
                    GuidValue(guidAttribute.Value)))))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                .WithLeadingTrivia(ParseLeadingTrivia($"/// <summary>The IID guid for this interface.</summary>\n/// <value>{guidAttributeValue:B}</value>\n")));

            if (this.TryDeclareCOMGuidInterfaceIfNecessary())
            {
                baseTypes.Add(SimpleBaseType(IComIIDGuidInterfaceName));

                IdentifierNameSyntax dataLocal = IdentifierName("data");

                // Rather than just `return ref IID_Guid`, which returns a pointer to a 'movable' field,
                // We leverage C# syntax that we know the modern C# compiler will turn into a pointer directly into the dll image,
                // so that the pointer does not move.
                // This does rely on at least the generated code running on a little endian machine, since we're laying raw bytes on top of integer fields.
                // But at the moment, we also assume this source generator is running on little endian for convenience for the reverse operation.
                if (!BitConverter.IsLittleEndian)
                {
                    throw new NotSupportedException("Conversion from big endian to little endian is not implemented.");
                }

                // ReadOnlySpan<byte> data = new byte[] { ... };
                ReadOnlySpan<byte> guidBytes = new((byte*)&guidAttributeValue, sizeof(Guid));
                LocalDeclarationStatementSyntax dataDecl = LocalDeclarationStatement(
                    VariableDeclaration(MakeReadOnlySpanOfT(PredefinedType(Token(SyntaxKind.ByteKeyword)))).AddVariables(
                        VariableDeclarator(dataLocal.Identifier).WithInitializer(EqualsValueClause(NewByteArray(guidBytes)))));

                // return ref Unsafe.As<byte, Guid>(ref MemoryMarshal.GetReference(data));
                ReturnStatementSyntax returnStatement = ReturnStatement(RefExpression(
                    InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(nameof(Unsafe)),
                            GenericName(nameof(Unsafe.As)).AddTypeArgumentListArguments(PredefinedType(Token(SyntaxKind.ByteKeyword)), IdentifierName(nameof(Guid)))),
                        ArgumentList().AddArguments(
                            Argument(
                                InvocationExpression(
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(MemoryMarshal)), IdentifierName(nameof(MemoryMarshal.GetReference))),
                                    ArgumentList(SingletonSeparatedList(Argument(dataLocal)))))
                            .WithRefOrOutKeyword(Token(SyntaxKind.RefKeyword))))));

                // The native assembly code for this property getter is just a `mov` and a `ret.
                // For our callers to also enjoy just the `mov` instruction, we have to attribute for aggressive inlining.
                // [MethodImpl(MethodImplOptions.AggressiveInlining)]
                AttributeListSyntax methodImplAttr = AttributeList().AddAttributes(MethodImpl(MethodImplOptions.AggressiveInlining));

                BlockSyntax getBody = Block(dataDecl, returnStatement);

                // static ref readonly Guid IComIID.Guid { get { ... } }
                PropertyDeclarationSyntax guidProperty = PropertyDeclaration(IdentifierName(nameof(Guid)).WithTrailingTrivia(Space), ComIIDGuidPropertyName.Identifier)
                    .WithExplicitInterfaceSpecifier(ExplicitInterfaceSpecifier(IComIIDGuidInterfaceName))
                    .AddModifiers(TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.RefKeyword), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                    .WithAccessorList(AccessorList().AddAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, getBody).AddAttributeLists(methodImplAttr)));
                members.Add(guidProperty);
            }
        }

        return (members, baseTypes);
    }

    private bool UsePreserveSigForComMethod(MethodDefinition methodDefinition, MethodSignature<TypeHandleInfo> signature, string ifaceName, string methodName)
    {
        return !IsHresult(signature.ReturnType)
            || (methodDefinition.ImplAttributes & MethodImplAttributes.PreserveSig) == MethodImplAttributes.PreserveSig
            || this.options.ComInterop.PreserveSigMethods.Contains("*")
            || this.FindInteropDecorativeAttribute(methodDefinition.GetCustomAttributes(), CanReturnMultipleSuccessValuesAttribute) is not null
            || this.FindInteropDecorativeAttribute(methodDefinition.GetCustomAttributes(), CanReturnErrorsAsSuccessAttribute) is not null
            || this.options.ComInterop.PreserveSigMethods.Contains($"{ifaceName}.{methodName}")
            || this.options.ComInterop.PreserveSigMethods.Contains(ifaceName.ToString());
    }

    private ISet<string> GetDeclarableProperties(IEnumerable<QualifiedMethodDefinition> methods, string ifaceName, bool allowNonConsecutiveAccessors, Context context)
    {
        Dictionary<string, (TypeSyntax Type, int Index)> goodProperties = new(StringComparer.Ordinal);
        HashSet<string> badProperties = new(StringComparer.Ordinal);
        int rowIndex = -1;
        foreach (QualifiedMethodDefinition methodDefinition in methods)
        {
            rowIndex++;
            if (methodDefinition.Generator.TryGetPropertyAccessorInfo(methodDefinition.Method, ifaceName, context, out IdentifierNameSyntax? propertyName, out SyntaxKind? accessorKind, out TypeSyntax? propertyType))
            {
                if (badProperties.Contains(propertyName.Identifier.ValueText))
                {
                    continue;
                }

                if (goodProperties.TryGetValue(propertyName.Identifier.ValueText, out var priorPropertyData))
                {
                    bool badProperty = false;
                    badProperty |= priorPropertyData.Type.ToString() != propertyType.ToString();
                    badProperty |= !allowNonConsecutiveAccessors && priorPropertyData.Index != rowIndex - 1;
                    if (badProperty)
                    {
                        ReportBadProperty();
                        continue;
                    }
                }

                goodProperties[propertyName.Identifier.ValueText] = (propertyType, rowIndex);
            }
            else if (propertyName is not null)
            {
                ReportBadProperty();
            }

            void ReportBadProperty()
            {
                badProperties.Add(propertyName.Identifier.ValueText);
                goodProperties.Remove(propertyName.Identifier.ValueText);
            }
        }

        return goodProperties.Count == 0 ? ImmutableHashSet<string>.Empty : new HashSet<string>(goodProperties.Keys, StringComparer.Ordinal);
    }

    private bool TryGetPropertyAccessorInfo(MethodDefinition methodDefinition, string ifaceName, Context context, [NotNullWhen(true)] out IdentifierNameSyntax? propertyName, [NotNullWhen(true)] out SyntaxKind? accessorKind, [NotNullWhen(true)] out TypeSyntax? propertyType)
    {
        propertyName = null;
        accessorKind = null;
        propertyType = null;
        TypeSyntaxSettings syntaxSettings = context.Filter(this.comSignatureTypeSettings);

        if ((methodDefinition.Attributes & MethodAttributes.SpecialName) != MethodAttributes.SpecialName)
        {
            // Sometimes another method actually *does* qualify as a property accessor, which would have this method as another accessor if it qualified.
            // But since this doesn't qualify, produce the property name that should be disqualified as a whole since C# doesn't like seeing property X and method set_X as seperate declarations.
            string disqualifiedMethodName = this.Reader.GetString(methodDefinition.Name);
            int index = disqualifiedMethodName.IndexOf('_');
            if (index > 0)
            {
                propertyName = IdentifierName(disqualifiedMethodName.Substring(index + 1));
            }

            return false;
        }

        MethodSignature<TypeHandleInfo> signature = methodDefinition.DecodeSignature(SignatureHandleProvider.Instance, null);
        string methodName = this.Reader.GetString(methodDefinition.Name);
        if (this.UsePreserveSigForComMethod(methodDefinition, signature, ifaceName, methodName))
        {
            return false;
        }

        ParameterHandleCollection parameters = methodDefinition.GetParameters();
        if (parameters.Count != 2)
        {
            return false;
        }

        const string getterPrefix = "get_";
        const string putterPrefix = "put_";
        const string setterPrefix = "set_";
        bool isGetter = methodName.StartsWith(getterPrefix, StringComparison.Ordinal);
        bool isPutter = methodName.StartsWith(putterPrefix, StringComparison.Ordinal);
        bool isSetter = methodName.StartsWith(setterPrefix, StringComparison.Ordinal);

        if (isGetter || isPutter || isSetter)
        {
            if (!IsHresult(signature.ReturnType))
            {
                return false;
            }

            Parameter propertyTypeParameter = this.Reader.GetParameter(parameters.Skip(1).Single());
            TypeHandleInfo propertyTypeInfo = signature.ParameterTypes[0];
            propertyType = propertyTypeInfo.ToTypeSyntax(syntaxSettings, GeneratingElement.Property, propertyTypeParameter.GetCustomAttributes(), propertyTypeParameter.Attributes).Type;

            if (isGetter)
            {
                propertyName = SafeIdentifierName(methodName.Substring(getterPrefix.Length));
                accessorKind = SyntaxKind.GetAccessorDeclaration;

                if ((propertyTypeParameter.Attributes & ParameterAttributes.Out) != ParameterAttributes.Out)
                {
                    return false;
                }

                if (propertyType is PointerTypeSyntax propertyTypePointer && (syntaxSettings.AllowMarshaling || !this.IsManagedType(propertyTypeInfo)))
                {
                    propertyType = propertyTypePointer.ElementType;
                }

                return true;
            }

            if (isSetter || isPutter)
            {
                Debug.Assert(setterPrefix.Length == putterPrefix.Length, "If these lengths do not equal, our Substring math may be wrong.");
                propertyName = SafeIdentifierName(methodName.Substring(setterPrefix.Length));
                accessorKind = SyntaxKind.SetAccessorDeclaration;
                return true;
            }
        }

        return false;
    }

    private CustomAttribute? FindGuidAttribute(CustomAttributeHandleCollection attributes) => this.FindInteropDecorativeAttribute(attributes, nameof(GuidAttribute));

    private Guid? FindGuidFromAttribute(TypeDefinition typeDef) => this.FindGuidFromAttribute(typeDef.GetCustomAttributes());

    private Guid? FindGuidFromAttribute(CustomAttributeHandleCollection attributes) => this.FindGuidAttribute(attributes) is CustomAttribute att ? (Guid?)DecodeGuidFromAttribute(att) : null;

    private bool TryDeclareCOMGuidInterfaceIfNecessary()
    {
        // Static interface members require C# 11 and .NET 7 at minimum.
        if (!this.IsFeatureAvailable(Feature.InterfaceStaticMembers))
        {
            return false;
        }

        if (this.comIIDInterfacePredefined)
        {
            return true;
        }

        this.volatileCode.GenerateSpecialType(IComIIDGuidInterfaceName.Identifier.ValueText, delegate
        {
            // internal static abstract ref readonly Guid Guid { get; }
            PropertyDeclarationSyntax guidProperty = PropertyDeclaration(IdentifierName(nameof(Guid)).WithTrailingTrivia(Space), ComIIDGuidPropertyName.Identifier)
                .AddModifiers(
                    TokenWithSpace(this.Visibility),
                    TokenWithSpace(SyntaxKind.StaticKeyword),
                    TokenWithSpace(SyntaxKind.AbstractKeyword),
                    TokenWithSpace(SyntaxKind.RefKeyword),
                    TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                .WithAccessorList(AccessorList().AddAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Semicolon)))
                .WithLeadingTrivia(ParseLeadingTrivia($"/// <summary>The IID guid for this interface.</summary>\n/// <remarks>The <see cref=\"Guid\" /> reference that is returned comes from a permanent memory address, and is therefore safe to convert to a pointer and pass around or hold long-term.</remarks>\n"));

            // internal interface IComIID { ... }
            InterfaceDeclarationSyntax ifaceDecl = InterfaceDeclaration(IComIIDGuidInterfaceName.Identifier)
                .AddModifiers(Token(this.Visibility))
                .AddMembers(guidProperty);

            this.volatileCode.AddSpecialType(IComIIDGuidInterfaceName.Identifier.ValueText, ifaceDecl);
        });

        return true;
    }

    /// <summary>
    /// Creates an empty class that when instantiated, creates a cocreatable Windows object
    /// that may implement a number of interfaces at runtime, discoverable only by documentation.
    /// </summary>
    private ClassDeclarationSyntax DeclareCocreatableClass(TypeDefinition typeDef)
    {
        IdentifierNameSyntax name = IdentifierName(this.Reader.GetString(typeDef.Name));
        Guid guid = this.FindGuidFromAttribute(typeDef) ?? throw new ArgumentException("Type does not have a GuidAttribute.");
        SyntaxTokenList classModifiers = TokenList(TokenWithSpace(this.Visibility));
        classModifiers = classModifiers.Add(TokenWithSpace(SyntaxKind.PartialKeyword));
        ClassDeclarationSyntax result = ClassDeclaration(name.Identifier)
            .WithModifiers(classModifiers)
            .AddAttributeLists(AttributeList().AddAttributes(GUID(guid), ComImportAttributeSyntax));

        result = this.AddApiDocumentation(name.Identifier.ValueText, result);
        return result;
    }
}
