// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    private enum FriendlyOverloadOf
    {
        ExternMethod,
        StructMethod,
        InterfaceMethod,
    }

    private IEnumerable<MethodDeclarationSyntax> DeclareFriendlyOverloads(MethodDefinition methodDefinition, MethodDeclarationSyntax externMethodDeclaration, NameSyntax declaringTypeName, FriendlyOverloadOf overloadOf, HashSet<string> helperMethodsAdded)
    {
        // If/when we ever need helper methods for the friendly overloads again, they can be added when used with code like this:
        ////if (helperMethodsAdded.Add(SomeHelperMethodName))
        ////{
        ////    yield return PInvokeHelperMethods[SomeHelperMethodName];
        ////}

        if (this.TryFetchTemplate(externMethodDeclaration.Identifier.ValueText, out MemberDeclarationSyntax? templateFriendlyOverload))
        {
            yield return (MethodDeclarationSyntax)templateFriendlyOverload;
        }

        if (externMethodDeclaration.Identifier.ValueText != "CoCreateInstance" || !this.options.ComInterop.UseIntPtrForComOutPointers)
        {
            if (this.options.AllowMarshaling && this.TryFetchTemplate("marshaling/" + externMethodDeclaration.Identifier.ValueText, out templateFriendlyOverload))
            {
                yield return (MethodDeclarationSyntax)templateFriendlyOverload;
            }

            if (!this.options.AllowMarshaling && this.TryFetchTemplate("no_marshaling/" + externMethodDeclaration.Identifier.ValueText, out templateFriendlyOverload))
            {
                yield return (MethodDeclarationSyntax)templateFriendlyOverload;
            }
        }

#pragma warning disable SA1114 // Parameter list should follow declaration
        static ParameterSyntax StripAttributes(ParameterSyntax parameter) => parameter.WithAttributeLists(List<AttributeListSyntax>());
        static ExpressionSyntax GetSpanLength(ExpressionSyntax span) => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, span, IdentifierName(nameof(Span<int>.Length)));
        bool isReleaseMethod = this.MetadataIndex.ReleaseMethods.Contains(externMethodDeclaration.Identifier.ValueText);
        bool doNotRelease = this.FindInteropDecorativeAttribute(this.GetReturnTypeCustomAttributes(methodDefinition), DoNotReleaseAttribute) is not null;

        TypeSyntaxSettings parameterTypeSyntaxSettings = overloadOf switch
        {
            FriendlyOverloadOf.ExternMethod => this.externSignatureTypeSettings,
            FriendlyOverloadOf.StructMethod => this.extensionMethodSignatureTypeSettings,
            FriendlyOverloadOf.InterfaceMethod => this.extensionMethodSignatureTypeSettings,
            _ => throw new NotSupportedException(overloadOf.ToString()),
        };

        MethodSignature<TypeHandleInfo> originalSignature = methodDefinition.DecodeSignature(SignatureHandleProvider.Instance, null);
        var parameters = externMethodDeclaration.ParameterList.Parameters.Select(StripAttributes).ToList();
        var lengthParamUsedBy = new Dictionary<int, int>();
        var parametersToRemove = new List<int>();
        var arguments = externMethodDeclaration.ParameterList.Parameters.Select(p => Argument(IdentifierName(p.Identifier.Text)).WithRefKindKeyword(p.Modifiers.FirstOrDefault(p => p.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword or SyntaxKind.InKeyword))).ToList();
        TypeSyntax? externMethodReturnType = externMethodDeclaration.ReturnType.WithoutLeadingTrivia();
        var fixedBlocks = new List<VariableDeclarationSyntax>();
        var leadingOutsideTryStatements = new List<StatementSyntax>();
        var leadingStatements = new List<StatementSyntax>();
        var trailingStatements = new List<StatementSyntax>();
        var finallyStatements = new List<StatementSyntax>();
        bool signatureChanged = false;
        foreach (ParameterHandle paramHandle in methodDefinition.GetParameters())
        {
            Parameter param = this.Reader.GetParameter(paramHandle);
            if (param.SequenceNumber == 0 || param.SequenceNumber - 1 >= parameters.Count)
            {
                continue;
            }

            bool isOptional = (param.Attributes & ParameterAttributes.Optional) == ParameterAttributes.Optional;
            bool isReserved = this.FindInteropDecorativeAttribute(param.GetCustomAttributes(), "ReservedAttribute") is not null;
            isOptional |= isReserved; // Per metadata decision made at https://github.com/microsoft/win32metadata/issues/1421#issuecomment-1372608090
            bool isIn = (param.Attributes & ParameterAttributes.In) == ParameterAttributes.In;
            bool isConst = this.FindInteropDecorativeAttribute(param.GetCustomAttributes(), "ConstAttribute") is not null;
            bool isComOutPtr = this.FindInteropDecorativeAttribute(param.GetCustomAttributes(), "ComOutPtrAttribute") is not null;
            bool isOut = isComOutPtr || (param.Attributes & ParameterAttributes.Out) == ParameterAttributes.Out;

            // TODO:
            // * Review double/triple pointer scenarios.
            //   * Consider CredEnumerateA, which is a "pointer to an array of pointers" (3-asterisks!). How does FriendlyAttribute improve this, if at all? The memory must be freed through another p/invoke.
            ParameterSyntax externParam = parameters[param.SequenceNumber - 1];
            if (externParam.Type is null)
            {
                throw new GenerationFailedException();
            }

            TypeHandleInfo parameterTypeInfo = originalSignature.ParameterTypes[param.SequenceNumber - 1];
            bool isManagedParameterType = this.IsManagedType(parameterTypeInfo);
            IdentifierNameSyntax origName = IdentifierName(externParam.Identifier.ValueText);

            if (isReserved && !isOut)
            {
                // Remove the parameter and supply the default value for the type to the extern method.
                arguments[param.SequenceNumber - 1] = Argument(LiteralExpression(SyntaxKind.DefaultLiteralExpression));
                parametersToRemove.Add(param.SequenceNumber - 1);
                signatureChanged = true;
            }
            else if (isManagedParameterType && (externParam.Modifiers.Any(SyntaxKind.OutKeyword) || externParam.Modifiers.Any(SyntaxKind.RefKeyword)))
            {
                bool hasOut = externParam.Modifiers.Any(SyntaxKind.OutKeyword);
                arguments[param.SequenceNumber - 1] = arguments[param.SequenceNumber - 1].WithRefKindKeyword(TokenWithSpace(hasOut ? SyntaxKind.OutKeyword : SyntaxKind.RefKeyword));
            }
            else if (isOut && !isIn && !isReleaseMethod && parameterTypeInfo is PointerTypeHandleInfo { ElementType: HandleTypeHandleInfo pointedElementInfo } && this.TryGetHandleReleaseMethod(pointedElementInfo.Handle, out string? outReleaseMethod) && !this.Reader.StringComparer.Equals(methodDefinition.Name, outReleaseMethod))
            {
                if (this.RequestSafeHandle(outReleaseMethod) is TypeSyntax safeHandleType)
                {
                    signatureChanged = true;

                    IdentifierNameSyntax typeDefHandleName = IdentifierName(externParam.Identifier.ValueText + "Local");

                    // out SafeHandle
                    parameters[param.SequenceNumber - 1] = externParam
                        .WithType(safeHandleType.WithTrailingTrivia(TriviaList(Space)))
                        .WithModifiers(TokenList(TokenWithSpace(SyntaxKind.OutKeyword)));

                    // HANDLE SomeLocal;
                    leadingStatements.Add(LocalDeclarationStatement(VariableDeclaration(pointedElementInfo.ToTypeSyntax(parameterTypeSyntaxSettings, null).Type).AddVariables(
                        VariableDeclarator(typeDefHandleName.Identifier))));

                    // Argument: &SomeLocal
                    arguments[param.SequenceNumber - 1] = Argument(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, typeDefHandleName));

                    // Some = new SafeHandle(SomeLocal, ownsHandle: true);
                    trailingStatements.Add(ExpressionStatement(AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        origName,
                        ObjectCreationExpression(safeHandleType).AddArgumentListArguments(
                            Argument(GetIntPtrFromTypeDef(typeDefHandleName, pointedElementInfo)),
                            Argument(LiteralExpression(doNotRelease ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression)).WithNameColon(NameColon(IdentifierName("ownsHandle")))))));
                }
            }
            else if (this.options.UseSafeHandles && isIn && !isOut && !isReleaseMethod && parameterTypeInfo is HandleTypeHandleInfo parameterHandleTypeInfo && this.TryGetHandleReleaseMethod(parameterHandleTypeInfo.Handle, out string? releaseMethod) && !this.Reader.StringComparer.Equals(methodDefinition.Name, releaseMethod)
                && !(this.TryGetTypeDefFieldType(parameterHandleTypeInfo, out TypeHandleInfo? fieldType) && !this.IsSafeHandleCompatibleTypeDefFieldType(fieldType)))
            {
                IdentifierNameSyntax typeDefHandleName = IdentifierName(externParam.Identifier.ValueText + "Local");
                signatureChanged = true;

                IdentifierNameSyntax refAddedName = IdentifierName(externParam.Identifier.ValueText + "AddRef");

                // bool hParamNameAddRef = false;
                leadingOutsideTryStatements.Add(LocalDeclarationStatement(
                    VariableDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword))).AddVariables(
                        VariableDeclarator(refAddedName.Identifier).WithInitializer(EqualsValueClause(LiteralExpression(SyntaxKind.FalseLiteralExpression))))));

                // HANDLE hTemplateFileLocal;
                leadingStatements.Add(LocalDeclarationStatement(VariableDeclaration(externParam.Type).AddVariables(
                    VariableDeclarator(typeDefHandleName.Identifier))));

                // throw new ArgumentNullException(nameof(hTemplateFile));
                StatementSyntax nullHandleStatement = ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentNullException))).WithArgumentList(ArgumentList().AddArguments(Argument(NameOfExpression(IdentifierName(externParam.Identifier.ValueText))))));
                if (isOptional)
                {
                    HashSet<IntPtr> invalidValues = this.GetInvalidHandleValues(parameterHandleTypeInfo.Handle);
                    if (invalidValues.Count > 0)
                    {
                        // (HANDLE)new IntPtr(-1);
                        IntPtr invalidValue = GetPreferredInvalidHandleValue(invalidValues);
                        ExpressionSyntax invalidExpression = CastExpression(externParam.Type, IntPtrExpr(invalidValue));

                        // hTemplateFileLocal = invalid-handle-value;
                        nullHandleStatement = ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, typeDefHandleName, invalidExpression));
                    }
                }

                // if (hTemplateFile is object)
                leadingStatements.Add(IfStatement(
                    BinaryExpression(SyntaxKind.IsExpression, origName, PredefinedType(Token(SyntaxKind.ObjectKeyword))),
                    Block().AddStatements(
                    //// hTemplateFile.DangerousAddRef(ref hTemplateFileAddRef);
                    ExpressionStatement(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(SafeHandle.DangerousAddRef))))
                        .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(refAddedName).WithRefKindKeyword(TokenWithSpace(SyntaxKind.RefKeyword)))))),
                    //// hTemplateFileLocal = (HANDLE)hTemplateFile.DangerousGetHandle();
                    ExpressionStatement(
                        AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            typeDefHandleName,
                            CastExpression(
                                externParam.Type.WithoutTrailingTrivia(),
                                InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(SafeHandle.DangerousGetHandle))), ArgumentList())))
                        .WithOperatorToken(TokenWithSpaces(SyntaxKind.EqualsToken)))),
                    //// else hTemplateFileLocal = default;
                    ElseClause(nullHandleStatement)));

                // if (hTemplateFileAddRef)
                //     hTemplateFile.DangerousRelease();
                finallyStatements.Add(
                    IfStatement(
                        refAddedName,
                        ExpressionStatement(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(SafeHandle.DangerousRelease))), ArgumentList())))
                    .WithCloseParenToken(TokenWithLineFeed(SyntaxKind.CloseParenToken)));

                // Accept the SafeHandle instead.
                parameters[param.SequenceNumber - 1] = externParam
                    .WithType(IdentifierName(nameof(SafeHandle)).WithTrailingTrivia(TriviaList(Space)));

                // hParamNameLocal;
                arguments[param.SequenceNumber - 1] = Argument(typeDefHandleName);
            }
            else if ((externParam.Type is PointerTypeSyntax { ElementType: TypeSyntax ptrElementType }
                && !IsVoid(ptrElementType)
                && !this.IsInterface(parameterTypeInfo)
                && this.canUseSpan) ||
                externParam.Type is ArrayTypeSyntax)
            {
                TypeSyntax elementType = externParam.Type is PointerTypeSyntax ptr ? ptr.ElementType
                    : externParam.Type is ArrayTypeSyntax array ? array.ElementType
                    : throw new InvalidOperationException();
                bool isPointerToPointer = elementType is PointerTypeSyntax or FunctionPointerTypeSyntax;

                // If there are no SAL annotations at all...
                if (!isOptional && !isIn && !isOut)
                {
                    // Consider that const means [In]
                    if (isConst)
                    {
                        isIn = true;
                        isOut = false;
                    }
                    else
                    {
                        // Otherwise assume bidirectional.
                        isIn = isOut = true;
                    }
                }

                bool isArray = false;
                bool isNullTerminated = false; // TODO
                short? sizeParamIndex = null;
                int? sizeConst = null;
                if (this.FindInteropDecorativeAttribute(param.GetCustomAttributes(), NativeArrayInfoAttribute) is CustomAttribute att)
                {
                    isArray = true;
                    NativeArrayInfo nativeArrayInfo = DecodeNativeArrayInfoAttribute(att);
                    sizeParamIndex = nativeArrayInfo.CountParamIndex;
                    sizeConst = nativeArrayInfo.CountConst;
                }

                IdentifierNameSyntax localName = IdentifierName(origName + "Local");
                if (isArray)
                {
                    // TODO: add support for in/out size parameters. (e.g. RSGetViewports)
                    // TODO: add support for lists of pointers via a generated pointer-wrapping struct (e.g. PSSetSamplers)

                    // It is possible that sizeParamIndex points to a parameter that is not on the extern method
                    // when the parameter is the last one and was moved to a return value.
                    if (sizeParamIndex.HasValue
                        && externMethodDeclaration.ParameterList.Parameters.Count > sizeParamIndex.Value
                        && !(externMethodDeclaration.ParameterList.Parameters[sizeParamIndex.Value].Type is PointerTypeSyntax)
                        && !(externMethodDeclaration.ParameterList.Parameters[sizeParamIndex.Value].Modifiers.Any(SyntaxKind.OutKeyword) || externMethodDeclaration.ParameterList.Parameters[sizeParamIndex.Value].Modifiers.Any(SyntaxKind.RefKeyword))
                        && !isPointerToPointer)
                    {
                        signatureChanged = true;

                        if (lengthParamUsedBy.TryGetValue(sizeParamIndex.Value, out int userIndex))
                        {
                            // Multiple array parameters share a common 'length' parameter.
                            // Since we're making this a little less obvious, add a quick if check in the helper method
                            // that enforces that all such parameters have a common span length.
                            ExpressionSyntax otherUserName = IdentifierName(parameters[userIndex].Identifier.ValueText);
                            leadingStatements.Add(IfStatement(
                                BinaryExpression(
                                    SyntaxKind.NotEqualsExpression,
                                    GetSpanLength(otherUserName),
                                    GetSpanLength(origName)),
                                ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentException))).WithArgumentList(ArgumentList()))));
                        }
                        else
                        {
                            lengthParamUsedBy.Add(sizeParamIndex.Value, param.SequenceNumber - 1);
                        }

                        if (externParam.Type is PointerTypeSyntax)
                        {
                            parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                                .WithType((isIn && isConst ? MakeReadOnlySpanOfT(elementType) : MakeSpanOfT(elementType)).WithTrailingTrivia(TriviaList(Space)));
                            fixedBlocks.Add(VariableDeclaration(externParam.Type).AddVariables(
                                VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(origName))));
                            arguments[param.SequenceNumber - 1] = Argument(localName);
                        }

                        ExpressionSyntax sizeArgExpression = GetSpanLength(origName);
                        if (!(parameters[sizeParamIndex.Value].Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.IntKeyword } }))
                        {
                            sizeArgExpression = CastExpression(parameters[sizeParamIndex.Value].Type!, sizeArgExpression);
                        }

                        arguments[sizeParamIndex.Value] = Argument(sizeArgExpression);
                    }
                    else if (sizeConst.HasValue && !isPointerToPointer && this.canUseSpan && externParam.Type is PointerTypeSyntax)
                    {
                        // TODO: add support for lists of pointers via a generated pointer-wrapping struct
                        signatureChanged = true;

                        // Accept a span instead of a pointer.
                        parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                            .WithType((isIn && isConst ? MakeReadOnlySpanOfT(elementType) : MakeSpanOfT(elementType)).WithTrailingTrivia(TriviaList(Space)));
                        fixedBlocks.Add(VariableDeclaration(externParam.Type).AddVariables(
                            VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(origName))));
                        arguments[param.SequenceNumber - 1] = Argument(localName);

                        // Add a runtime check that the span is at least the required length.
                        leadingStatements.Add(IfStatement(
                            BinaryExpression(
                                SyntaxKind.LessThanExpression,
                                GetSpanLength(origName),
                                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(sizeConst.Value))),
                            ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentException))).WithArgumentList(ArgumentList()))));
                    }
                    else if (isNullTerminated && isConst && parameters[param.SequenceNumber - 1].Type is PointerTypeSyntax { ElementType: PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.CharKeyword } } })
                    {
                        // replace char* with string
                        signatureChanged = true;
                        parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                            .WithType(PredefinedType(TokenWithSpace(SyntaxKind.StringKeyword)));
                        fixedBlocks.Add(VariableDeclaration(externParam.Type).AddVariables(
                            VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(origName))));
                        arguments[param.SequenceNumber - 1] = Argument(localName);
                    }
                }
                else if (isIn && isOptional && !isOut && !isPointerToPointer)
                {
                    signatureChanged = true;
                    parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                        .WithType(NullableType(elementType).WithTrailingTrivia(TriviaList(Space)));
                    leadingStatements.Add(
                        LocalDeclarationStatement(VariableDeclaration(elementType)
                            .AddVariables(VariableDeclarator(localName.Identifier).WithInitializer(
                                EqualsValueClause(
                                    BinaryExpression(SyntaxKind.CoalesceExpression, origName, DefaultExpression(elementType)))))));
                    arguments[param.SequenceNumber - 1] = Argument(ConditionalExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName("HasValue")),
                        PrefixUnaryExpression(SyntaxKind.AddressOfExpression, localName),
                        LiteralExpression(SyntaxKind.NullLiteralExpression)));
                }
                else if (isIn && isOut && !isOptional)
                {
                    signatureChanged = true;
                    parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                        .WithType(elementType.WithTrailingTrivia(TriviaList(Space)))
                        .WithModifiers(TokenList(TokenWithSpace(SyntaxKind.RefKeyword)));
                    fixedBlocks.Add(VariableDeclaration(externParam.Type).AddVariables(
                        VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(
                            PrefixUnaryExpression(SyntaxKind.AddressOfExpression, origName)))));
                    arguments[param.SequenceNumber - 1] = Argument(localName);
                }
                else if (isOut && !isIn && !isOptional)
                {
                    signatureChanged = true;
                    parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                        .WithType(elementType.WithTrailingTrivia(TriviaList(Space)))
                        .WithModifiers(TokenList(TokenWithSpace(SyntaxKind.OutKeyword)));
                    fixedBlocks.Add(VariableDeclaration(externParam.Type).AddVariables(
                        VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(
                            PrefixUnaryExpression(SyntaxKind.AddressOfExpression, origName)))));
                    arguments[param.SequenceNumber - 1] = Argument(localName);
                }
                else if (isIn && !isOut && !isOptional)
                {
                    // Use the "in" modifier to avoid copying the struct.
                    signatureChanged = true;
                    parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                        .WithType(elementType.WithTrailingTrivia(TriviaList(Space)))
                        .WithModifiers(TokenList(TokenWithSpace(SyntaxKind.InKeyword)));
                    fixedBlocks.Add(VariableDeclaration(externParam.Type).AddVariables(
                        VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(
                            PrefixUnaryExpression(SyntaxKind.AddressOfExpression, origName)))));
                    arguments[param.SequenceNumber - 1] = Argument(localName);
                }
            }
            else if (isIn && !isOut && isConst && externParam.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PCWSTR" } } })
            {
                IdentifierNameSyntax localName = IdentifierName(origName + "Local");
                signatureChanged = true;
                parameters[param.SequenceNumber - 1] = externParam
                    .WithType(PredefinedType(TokenWithSpace(SyntaxKind.StringKeyword)));
                fixedBlocks.Add(VariableDeclaration(PointerType(PredefinedType(Token(SyntaxKind.CharKeyword)))).AddVariables(
                    VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(origName))));
                arguments[param.SequenceNumber - 1] = Argument(localName);
            }
            else if (isIn && !isOut && isConst && externParam.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PCSTR" } } })
            {
                IdentifierNameSyntax localName = IdentifierName(origName + "Local");
                signatureChanged = true;
                parameters[param.SequenceNumber - 1] = externParam
                    .WithType(PredefinedType(TokenWithSpace(SyntaxKind.StringKeyword)));

                // fixed (byte* someLocal = some is object ? System.Text.Encoding.Default.GetBytes(some) : null)
                fixedBlocks.Add(VariableDeclaration(PointerType(PredefinedType(Token(SyntaxKind.ByteKeyword)))).AddVariables(
                    VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(
                        ConditionalExpression(
                            BinaryExpression(SyntaxKind.IsExpression, origName, PredefinedType(Token(SyntaxKind.ObjectKeyword))),
                            InvocationExpression(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    ParseTypeName("global::System.Text.Encoding.Default"),
                                    IdentifierName(nameof(Encoding.GetBytes))))
                            .WithArgumentList(
                                ArgumentList(
                                    SingletonSeparatedList(Argument(origName)))),
                            LiteralExpression(SyntaxKind.NullLiteralExpression))))));

                // new PCSTR(someLocal)
                arguments[param.SequenceNumber - 1] = Argument(ObjectCreationExpression(externParam.Type).AddArgumentListArguments(Argument(localName)));
            }
            else if (isIn && isOut && this.canUseSpan && externParam.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PWSTR" } } })
            {
                IdentifierNameSyntax localName = IdentifierName("p" + origName);
                IdentifierNameSyntax localWstrName = IdentifierName("wstr" + origName);
                signatureChanged = true;
                parameters[param.SequenceNumber - 1] = externParam
                    .WithType(MakeSpanOfT(PredefinedType(Token(SyntaxKind.CharKeyword))))
                    .AddModifiers(Token(SyntaxKind.RefKeyword));

                // fixed (char* pParam1 = Param1)
                fixedBlocks.Add(VariableDeclaration(PointerType(PredefinedType(Token(SyntaxKind.CharKeyword)))).AddVariables(
                    VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(
                        origName))));

                // wstrParam1
                arguments[param.SequenceNumber - 1] = Argument(localWstrName);

                // if (buffer.LastIndexOf('\0') == -1) throw new ArgumentException("Required null terminator is missing.", "Param1");
                InvocationExpressionSyntax lastIndexOf = InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(MemoryExtensions.LastIndexOf))),
                    ArgumentList().AddArguments(Argument(LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal('\0')))));
                leadingOutsideTryStatements.Add(IfStatement(
                    BinaryExpression(SyntaxKind.EqualsExpression, lastIndexOf, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(-1))),
                    ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentException))).AddArgumentListArguments(
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("Required null terminator missing."))),
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(externParam.Identifier.ValueText)))))));

                // PWSTR wstrParam1 = pParam1;
                leadingStatements.Add(LocalDeclarationStatement(
                    VariableDeclaration(externParam.Type).AddVariables(VariableDeclarator(localWstrName.Identifier).WithInitializer(EqualsValueClause(localName)))));

                // Param1 = Param1.Slice(0, wstrParam1.Length);
                trailingStatements.Add(ExpressionStatement(AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    origName,
                    InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(Span<char>.Slice))),
                        ArgumentList().AddArguments(
                            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
                            Argument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, localWstrName, IdentifierName("Length"))))))));
            }
            else if (isIn && isOptional && !isOut && isManagedParameterType && parameterTypeInfo is PointerTypeHandleInfo ptrInfo && ptrInfo.ElementType.IsValueType(parameterTypeSyntaxSettings) is true && this.canUseUnsafeAsRef)
            {
                // The extern method couldn't have exposed the parameter as a pointer because the type is managed.
                // It would have exposed as an `in` modifier, and non-optional. But we can expose as optional anyway.
                signatureChanged = true;
                IdentifierNameSyntax localName = IdentifierName(origName + "Local");
                parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                    .WithType(NullableType(externParam.Type).WithTrailingTrivia(TriviaList(Space)))
                    .WithModifiers(TokenList()); // drop the `in` modifier.
                leadingStatements.Add(
                    LocalDeclarationStatement(VariableDeclaration(externParam.Type)
                        .AddVariables(VariableDeclarator(localName.Identifier).WithInitializer(
                            EqualsValueClause(
                                BinaryExpression(SyntaxKind.CoalesceExpression, origName, DefaultExpression(externParam.Type)))))));

                // We can't pass in null, but we can be fancy to achieve the same effect.
                // Unsafe.NullRef<TParamType>() or Unsafe.AsRef<TParamType>(null), depending on what's available.
                ExpressionSyntax nullRef = this.canUseUnsafeNullRef
                    ? InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Unsafe)), GenericName("NullRef", TypeArgumentList().AddArguments(externParam.Type))),
                        ArgumentList())
                    : InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Unsafe)), GenericName(nameof(Unsafe.AsRef), TypeArgumentList().AddArguments(externParam.Type))),
                        ArgumentList().AddArguments(Argument(LiteralExpression(SyntaxKind.NullLiteralExpression))));
                arguments[param.SequenceNumber - 1] = Argument(ConditionalExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName("HasValue")),
                    localName,
                    nullRef));
            }
        }

        TypeSyntax? returnSafeHandleType = originalSignature.ReturnType is HandleTypeHandleInfo returnTypeHandleInfo
            && this.TryGetHandleReleaseMethod(returnTypeHandleInfo.Handle, out string? returnReleaseMethod)
            ? this.RequestSafeHandle(returnReleaseMethod) : null;
        SyntaxToken friendlyMethodName = externMethodDeclaration.Identifier;

        if (returnSafeHandleType is object && !signatureChanged)
        {
            // The parameter types are all the same, but we need a friendly overload with a different return type.
            // Our only choice is to rename the friendly overload.
            friendlyMethodName = Identifier(externMethodDeclaration.Identifier.ValueText + "_SafeHandle");
            signatureChanged = true;
        }

        if (signatureChanged)
        {
            // Remove in reverse order so as to not invalidate the indexes of elements to remove.
            // Also take care to only remove each element once, even if it shows up multiple times in the collection.
            SortedSet<int> parameterIndexesToRemove = new(lengthParamUsedBy.Keys);
            parameterIndexesToRemove.UnionWith(parametersToRemove);
            foreach (int indexToRemove in parameterIndexesToRemove.Reverse())
            {
                parameters.RemoveAt(indexToRemove);
            }

            TypeSyntax docRefExternName = overloadOf == FriendlyOverloadOf.InterfaceMethod
                ? QualifiedName(declaringTypeName, IdentifierName(externMethodDeclaration.Identifier))
                : IdentifierName(externMethodDeclaration.Identifier);
            SyntaxTrivia leadingTrivia = Trivia(
                DocumentationCommentTrivia(SyntaxKind.SingleLineDocumentationCommentTrivia).AddContent(
                    XmlText("/// "),
                    XmlEmptyElement("inheritdoc").AddAttributes(XmlCrefAttribute(NameMemberCref(docRefExternName, ToCref(externMethodDeclaration.ParameterList)))),
                    XmlText().AddTextTokens(XmlTextNewLine("\n", continueXmlDocumentationComment: false))));
            InvocationExpressionSyntax externInvocation = InvocationExpression(
                overloadOf switch
                {
                    FriendlyOverloadOf.ExternMethod => QualifiedName(declaringTypeName, IdentifierName(externMethodDeclaration.Identifier.Text)),
                    FriendlyOverloadOf.StructMethod => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName(externMethodDeclaration.Identifier.Text)),
                    FriendlyOverloadOf.InterfaceMethod => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("@this"), IdentifierName(externMethodDeclaration.Identifier.Text)),
                    _ => throw new NotSupportedException("Unrecognized friendly overload mode " + overloadOf),
                })
                .WithArgumentList(FixTrivia(ArgumentList().AddArguments(arguments.ToArray())));
            bool hasVoidReturn = externMethodReturnType is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.VoidKeyword } };
            BlockSyntax? body = Block().AddStatements(leadingStatements.ToArray());
            IdentifierNameSyntax resultLocal = IdentifierName("__result");
            if (returnSafeHandleType is object)
            {
                //// HANDLE result = invocation();
                body = body.AddStatements(LocalDeclarationStatement(VariableDeclaration(externMethodReturnType)
                    .AddVariables(VariableDeclarator(resultLocal.Identifier).WithInitializer(EqualsValueClause(externInvocation)))));

                body = body.AddStatements(trailingStatements.ToArray());

                //// return new SafeHandle(result, ownsHandle: true);
                body = body.AddStatements(ReturnStatement(ObjectCreationExpression(returnSafeHandleType).AddArgumentListArguments(
                    Argument(GetIntPtrFromTypeDef(resultLocal, originalSignature.ReturnType)),
                    Argument(LiteralExpression(doNotRelease ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression)).WithNameColon(NameColon(IdentifierName("ownsHandle"))))));
            }
            else if (hasVoidReturn)
            {
                body = body.AddStatements(ExpressionStatement(externInvocation));
                body = body.AddStatements(trailingStatements.ToArray());
            }
            else
            {
                // var result = externInvocation();
                body = body.AddStatements(LocalDeclarationStatement(VariableDeclaration(externMethodReturnType)
                    .AddVariables(VariableDeclarator(resultLocal.Identifier).WithInitializer(EqualsValueClause(externInvocation)))));

                body = body.AddStatements(trailingStatements.ToArray());

                // return result;
                body = body.AddStatements(ReturnStatement(resultLocal));
            }

            foreach (VariableDeclarationSyntax? fixedExpression in fixedBlocks)
            {
                body = Block(FixedStatement(fixedExpression, body).WithFixedKeyword(TokenWithSpace(SyntaxKind.FixedKeyword)));
            }

            if (finallyStatements.Count > 0)
            {
                body = Block()
                    .AddStatements(leadingOutsideTryStatements.ToArray())
                    .AddStatements(TryStatement(body, default, FinallyClause(Block().AddStatements(finallyStatements.ToArray()))));
            }
            else if (leadingOutsideTryStatements.Count > 0)
            {
                body = body.WithStatements(body.Statements.InsertRange(0, leadingOutsideTryStatements));
            }

            SyntaxTokenList modifiers = TokenList(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword));
            if (overloadOf != FriendlyOverloadOf.StructMethod)
            {
                modifiers = modifiers.Insert(1, TokenWithSpace(SyntaxKind.StaticKeyword));
            }

            if (overloadOf == FriendlyOverloadOf.InterfaceMethod)
            {
                parameters.Insert(0, Parameter(Identifier("@this")).WithType(declaringTypeName.WithTrailingTrivia(TriviaList(Space))).AddModifiers(TokenWithSpace(SyntaxKind.ThisKeyword)));
            }

            body = body
                .WithOpenBraceToken(Token(TriviaList(LineFeed), SyntaxKind.OpenBraceToken, TriviaList(LineFeed)))
                .WithCloseBraceToken(TokenWithLineFeed(SyntaxKind.CloseBraceToken));

            MethodDeclarationSyntax friendlyDeclaration = externMethodDeclaration
                .WithReturnType(externMethodReturnType.WithTrailingTrivia(TriviaList(Space)))
                .WithIdentifier(friendlyMethodName)
                .WithModifiers(modifiers)
                .WithAttributeLists(List<AttributeListSyntax>())
                .WithParameterList(FixTrivia(ParameterList().AddParameters(parameters.ToArray())))
                .WithBody(body)
                .WithSemicolonToken(default);

            if (returnSafeHandleType is object)
            {
                friendlyDeclaration = friendlyDeclaration.WithReturnType(returnSafeHandleType.WithTrailingTrivia(TriviaList(Space)));
            }

            if (this.GetSupportedOSPlatformAttribute(methodDefinition.GetCustomAttributes()) is AttributeSyntax supportedOSPlatformAttribute)
            {
                friendlyDeclaration = friendlyDeclaration.AddAttributeLists(AttributeList().AddAttributes(supportedOSPlatformAttribute));
            }

            friendlyDeclaration = friendlyDeclaration
                .WithLeadingTrivia(leadingTrivia);

            yield return friendlyDeclaration;
        }

        ExpressionSyntax GetIntPtrFromTypeDef(ExpressionSyntax typedefValue, TypeHandleInfo typeDefTypeInfo)
        {
            ExpressionSyntax intPtrValue = typedefValue;
            if (this.TryGetTypeDefFieldType(typeDefTypeInfo, out TypeHandleInfo? returnTypeField) && returnTypeField is PrimitiveTypeHandleInfo primitiveReturnField)
            {
                switch (primitiveReturnField.PrimitiveTypeCode)
                {
                    case PrimitiveTypeCode.UInt32:
                        // (IntPtr)result.Value;
                        intPtrValue = CastExpression(IntPtrTypeSyntax, MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, typedefValue, IdentifierName("Value")));
                        break;
                    case PrimitiveTypeCode.UIntPtr:
                        // unchecked((IntPtr)(long)(ulong)result.Value)
                        intPtrValue = UncheckedExpression(
                            CastExpression(
                                IntPtrTypeSyntax,
                                CastExpression(
                                    PredefinedType(Token(SyntaxKind.LongKeyword)),
                                    CastExpression(
                                        PredefinedType(Token(SyntaxKind.ULongKeyword)),
                                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, typedefValue, IdentifierName("Value"))))));
                        break;
                }
            }

            return intPtrValue;
        }
#pragma warning restore SA1114 // Parameter list should follow declaration
    }
}
