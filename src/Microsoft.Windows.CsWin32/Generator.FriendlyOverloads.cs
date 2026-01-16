// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    private static readonly TypeSyntax PCWSTRTypeSyntax = QualifiedName(QualifiedName(IdentifierName(GlobalWinmdRootNamespaceAlias), IdentifierName("Foundation")), IdentifierName("PCWSTR"));

    private enum FriendlyOverloadOf
    {
        ExternMethod,
        StructMethod,
        InterfaceMethod,
    }

    private static ParameterSyntax StripAttributes(ParameterSyntax parameter) => parameter.WithAttributeLists(default);

    private static ExpressionSyntax GetSpanLength(ExpressionSyntax span, bool isRefType) => isRefType ?
        ParenthesizedExpression(BinaryExpression(
                SyntaxKind.CoalesceExpression,
                ConditionalAccessExpression(span, IdentifierName(nameof(Span<>.Length))),
                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0)))) : MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, span, IdentifierName(nameof(Span<>.Length)));

    private static ExpressionSyntax GetIsSpanEmpty(ExpressionSyntax span, bool isRefType) => isRefType ?
        ParenthesizedExpression(BinaryExpression(
                SyntaxKind.EqualsExpression,
                span,
                LiteralExpression(SyntaxKind.NullLiteralExpression))) :
        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, span, IdentifierName(nameof(Span<>.IsEmpty)));

    private ExpressionSyntax GetIntPtrFromTypeDef(ExpressionSyntax typedefValue, TypeHandleInfo typeDefTypeInfo)
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

    private IEnumerable<MethodDeclarationSyntax> DeclareFriendlyOverloads(MethodDefinition methodDefinition, MethodDeclarationSyntax externMethodDeclaration, NameSyntax declaringTypeName, FriendlyOverloadOf overloadOf, HashSet<string> helperMethodsAdded, bool avoidWinmdRootAlias)
    {
        if (!this.options.FriendlyOverloads.Enabled)
        {
            yield break;
        }

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

        bool improvePointersToSpansAndRefs = this.canUseSpan;
        FriendlyMethodBookkeeping bookkeeping = new();
        foreach (MethodDeclarationSyntax method in this.DeclareFriendlyOverload(methodDefinition, externMethodDeclaration, declaringTypeName, overloadOf, helperMethodsAdded, avoidWinmdRootAlias, improvePointersToSpansAndRefs, omitOptionalParams: false, bookkeeping))
        {
            yield return method;
        }

        if (this.Options.FriendlyOverloads.IncludePointerOverloads && improvePointersToSpansAndRefs && bookkeeping.NumSpanByteParameters > 0)
        {
            // If we could use Span and _did_ use span Span and the pointer overloads were requested, then Generate overloads that use pointer types instead of Span<byte>/ReadOnlySpan<byte>.
            foreach (MethodDeclarationSyntax method in this.DeclareFriendlyOverload(methodDefinition, externMethodDeclaration, declaringTypeName, overloadOf, helperMethodsAdded, avoidWinmdRootAlias, improvePointersToSpansAndRefs: false, omitOptionalParams: false))
            {
                yield return method;
            }
        }
    }

    private IEnumerable<MethodDeclarationSyntax> DeclareFriendlyOverload(
        MethodDefinition methodDefinition,
        MethodDeclarationSyntax externMethodDeclaration,
        NameSyntax declaringTypeName,
        FriendlyOverloadOf overloadOf,
        HashSet<string> helperMethodsAdded,
        bool avoidWinmdRootAlias,
        bool improvePointersToSpansAndRefs,
        bool omitOptionalParams,
        FriendlyMethodBookkeeping? bookkeeping = null)
    {
#pragma warning disable SA1114 // Parameter list should follow declaration
        bool isReleaseMethod = this.MetadataIndex.ReleaseMethods.Contains(externMethodDeclaration.Identifier.ValueText);
        bool doNotRelease = this.FindInteropDecorativeAttribute(this.GetReturnTypeCustomAttributes(methodDefinition), DoNotReleaseAttribute) is not null;

        TypeSyntaxSettings parameterTypeSyntaxSettings = overloadOf switch
        {
            FriendlyOverloadOf.ExternMethod => this.externSignatureTypeSettings,
            FriendlyOverloadOf.StructMethod => this.extensionMethodSignatureTypeSettings,
            FriendlyOverloadOf.InterfaceMethod => this.extensionMethodSignatureTypeSettings,
            _ => throw new NotSupportedException(overloadOf.ToString()),
        };

        if (avoidWinmdRootAlias)
        {
            parameterTypeSyntaxSettings = parameterTypeSyntaxSettings with { AvoidWinmdRootAlias = true };
        }

        MethodSignature<TypeHandleInfo> originalSignature = methodDefinition.DecodeSignature(this.SignatureHandleProvider, null);
        CustomAttributeHandleCollection? returnTypeAttributes = null;
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
        bool signatureChanged = false; // Did the signature change with fundamentally different types?
        bool minorSignatureChange = false; // Did the signature change but not enough that overload resolution would be confused?
        List<Parameter>? countOfBytesStructParameters = null;
        int numOptionalParams = 0;
        int numSpanByteParameters = 0;
        SyntaxToken friendlyMethodName = externMethodDeclaration.Identifier;
        bool emulateMemberFunctionCallConv = friendlyMethodName.ValueText.EndsWith(EmulateMemberFunctionCallConvSuffix);

        foreach (ParameterHandle paramHandle in methodDefinition.GetParameters())
        {
            Parameter param = this.Reader.GetParameter(paramHandle);
            if (param.SequenceNumber == 0)
            {
                returnTypeAttributes = param.GetCustomAttributes();
            }

            if (param.SequenceNumber == 0 || param.SequenceNumber - 1 >= parameters.Count)
            {
                continue;
            }

            int origParamIndex = param.SequenceNumber - 1;
            int paramIndex = origParamIndex;

            if (emulateMemberFunctionCallConv)
            {
                // We added an additional parameter to the externMethodDeclaration which we need to adjust for.
                paramIndex++;
            }

            bool isOptional = (param.Attributes & ParameterAttributes.Optional) == ParameterAttributes.Optional;
            CustomAttributeHandleCollection paramAttributes = param.GetCustomAttributes();
            bool isReserved = this.FindInteropDecorativeAttribute(paramAttributes, "ReservedAttribute") is not null;
            bool isRetained = this.FindInteropDecorativeAttribute(paramAttributes, "RetainedAttribute") is not null;
            isOptional |= isReserved; // Per metadata decision made at https://github.com/microsoft/win32metadata/issues/1421#issuecomment-1372608090
            bool isIn = (param.Attributes & ParameterAttributes.In) == ParameterAttributes.In;
            bool isConst = this.FindInteropDecorativeAttribute(paramAttributes, "ConstAttribute") is not null;
            bool isComOutPtr = this.FindInteropDecorativeAttribute(paramAttributes, "ComOutPtrAttribute") is not null;
            bool isOut = isComOutPtr || (param.Attributes & ParameterAttributes.Out) == ParameterAttributes.Out;

            // TODO:
            // * Review double/triple pointer scenarios.
            //   * Consider CredEnumerateA, which is a "pointer to an array of pointers" (3-asterisks!). How does FriendlyAttribute improve this, if at all? The memory must be freed through another p/invoke.
            ParameterSyntax externParam = parameters[paramIndex];
            if (externParam.Type is null)
            {
                throw new GenerationFailedException();
            }

            TypeHandleInfo parameterTypeInfo = originalSignature.ParameterTypes[origParamIndex];
            bool isManagedParameterType = this.IsManagedType(parameterTypeInfo);
            MemorySize? memorySize = null;
            bool mustRemainAsPointer = false;
            bool isPointerToStructWithFlexibleArray = parameterTypeInfo is PointerTypeHandleInfo { ElementType: HandleTypeHandleInfo pointedElement } && pointedElement.Generator.IsStructWithFlexibleArray(pointedElement);
            if (this.FindInteropDecorativeAttribute(paramAttributes, MemorySizeAttribute) is CustomAttribute memorySizeAttribute)
            {
                memorySize = DecodeMemorySizeAttribute(memorySizeAttribute);
            }

            if (isRetained)
            {
                // Retained means that the callee will keep the pointer beyond this call. To communicate that safety problem to the caller,
                // the best we can do is project as a pointer so they know they need to think about it. See https://github.com/microsoft/CsWin32/issues/1066
                // and linked issues for more info.
                mustRemainAsPointer = true;
            }
            else if (memorySize is null)
            {
                // If there's no MemorySize attribute, we may still need to keep this parameter as a pointer if it's a struct with a flexible array.
                mustRemainAsPointer = isPointerToStructWithFlexibleArray;
            }
            else if (!improvePointersToSpansAndRefs)
            {
                // If we are generating the overload with pointers for memory sized params then also force them to pointers.
                mustRemainAsPointer = true;
            }

            if (isOptional && isIn && !isOut && externParam.Type is PointerTypeSyntax { ElementType: QualifiedNameSyntax elementTypeSyntax } && elementTypeSyntax.Right.Identifier.ValueText == "NativeOverlapped")
            {
                // OVERLAPPED struct must always be passed by pointer. Currently "in" optional parameters are promoted to nullable which
                // means the structs get copied. Normally this is fine since these struct addresses don't matter, but in the case of OVERLAPPED
                // it does. Trying to change "in" optional parameters to not be wrapped in nullable is a lot of work and impact for unclear value
                // so just adding special handling for OVERLAPPED for now.
                mustRemainAsPointer = true;
            }

            // For compat with how out/ref parameters used to be generated, leave out/ref parameters as pointers if we're not trying to improve them.
            if (isOptional && isOut && !isComOutPtr && !improvePointersToSpansAndRefs)
            {
                mustRemainAsPointer = true;
            }

            IdentifierNameSyntax origName = IdentifierName(externParam.Identifier.ValueText);

            bool isArray = false;
            bool isNullTerminated = false; // TODO
            bool isCountOfBytes = false;
            short? countParamIndex = null;
            int? countConst = null;
            if (this.FindInteropDecorativeAttribute(paramAttributes, NativeArrayInfoAttribute) is CustomAttribute nativeArrayInfoAttribute)
            {
                isArray = true;
                NativeArrayInfo nativeArrayInfo = DecodeNativeArrayInfoAttribute(nativeArrayInfoAttribute);
                countParamIndex = nativeArrayInfo.CountParamIndex;
                countConst = nativeArrayInfo.CountConst;
            }
            else if (externParam.Type is PointerTypeSyntax { ElementType: PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.ByteKeyword } })
            {
                // A very special case as documented in https://github.com/microsoft/win32metadata/issues/1555
                // where MemorySizeAttribute is applied to byte* parameters to indicate the size of the buffer.
                // Also https://github.com/microsoft/CsWin32/issues/1487 showed that byte* parameters are very unlikely to be a
                // single byte so it's safer to assume it's an un-annotated array.
                isArray = true;
                if (memorySize is not null)
                {
                    countParamIndex = memorySize.Value.BytesParamIndex;
                    isCountOfBytes = true;
                }
            }
            else if (memorySize is not null)
            {
                // Methods like InitializeAcl have a parameter typed as ACL but are sized more like a buffer. They accept
                // a Span<ACL> where the Length in bytes is based on a different parameter.
                isArray = true;
                countParamIndex = memorySize.Value.BytesParamIndex;
                isCountOfBytes = true;
            }

            bool projectAsSpanBytes = false;
            if (improvePointersToSpansAndRefs && IsVoidPtrOrPtrPtr(externParam.Type))
            {
                // if it's memory-sized project as Span<byte>
                if (memorySize is not null)
                {
                    isArray = true;
                    projectAsSpanBytes = true;
                }
                else if (!countParamIndex.HasValue && !isComOutPtr)
                {
                    // void* param with no size annotations and without [ComOutPtr] can't really be improved,
                    // so leave it alone.
                }
                else if (countParamIndex.HasValue)
                {
                    // If it's void* but annotated with a count-of-elements (like OfferVirtualMemory or TokenBindingGenerateMessage) then
                    // just leave it as raw pointer because it's not clear what the developer meant and projecting as Span<byte> will require
                    // manipulating the .Length parameter to preserve intent.
                    isArray = false;
                }
            }

            // Optional params which are going to be emitted as "out" or "ref" need to be part of a second overload where those parameters
            // are omitted so that there's a reasonably idiomatic way to pass "null" for them. Don't do this for all Optional params:
            // * Parameters that are [Reserved] are always omitted.
            // * Array parameters are projected as Span and empty/0 are the same as "null" at the ABI, so they also don't need to be omitted.
            // * If the parameter remains as pointer it won't be different for optional vs non-optional.
            bool omittableOptionalParam = false;
            SyntaxToken externParamModifier = externParam.Modifiers.FirstOrDefault(m => m.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword);
            if (isOptional && !isReserved && isOut && !isArray
                && externParamModifier == default
                && !mustRemainAsPointer)
            {
                // Keep track of how many out/ref optional parameters we included -- if there are any we will generate another overload with them omitted.
                numOptionalParams++;
                omittableOptionalParam = true;
            }

            if (mustRemainAsPointer)
            {
                // This block intentionally left blank, so as to disable further processing that might try to
                // replace a pointer with a `ref` or similar modifier.
            }
            else if (isReserved && !isOut)
            {
                // Remove the parameter and supply the default value for the type to the extern method.
                arguments[paramIndex] = Argument(LiteralExpression(SyntaxKind.DefaultLiteralExpression));
                parametersToRemove.Add(paramIndex);
                signatureChanged = true;
            }
            else if (omittableOptionalParam && omitOptionalParams)
            {
                // Remove the optional out parameter and supply the default value for the type to the extern method.
                if (externParamModifier.Kind() is SyntaxKind.OutKeyword or SyntaxKind.RefKeyword)
                {
                    if (externParam.Type is PointerTypeSyntax || externParam.Type is FunctionPointerTypeSyntax)
                    {
                        // Can't pass pointers as type parameter to Unsafe.NullRef<T>(), so use `ref *(delegate ...*)null` syntax instead.
                        ExpressionSyntax nullRef = PrefixUnaryExpression(SyntaxKind.PointerIndirectionExpression, CastExpression(PointerType(externParam.Type), LiteralExpression(SyntaxKind.NullLiteralExpression)));
                        arguments[paramIndex] = Argument(nullRef).WithRefKindKeyword(TokenWithSpace(externParamModifier.Kind()));
                    }
                    else
                    {
                        // ref Unsafe.NullRef<TParam>()
                        ExpressionSyntax nullRef = this.canUseUnsafeNullRef
                            ? InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Unsafe)), GenericName("NullRef", [externParam.Type])))
                            : InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Unsafe)), GenericName(nameof(Unsafe.AsRef), [externParam.Type])),
                                [Argument(LiteralExpression(SyntaxKind.NullLiteralExpression))]);
                        arguments[paramIndex] = Argument(nullRef).WithRefKindKeyword(TokenWithSpace(externParamModifier.Kind()));
                    }
                }
                else
                {
                    arguments[paramIndex] = Argument(DefaultExpression(externParam.Type));
                }

                parametersToRemove.Add(paramIndex);
                signatureChanged = true;
            }
            else if (isManagedParameterType && (externParam.Modifiers.Any(SyntaxKind.OutKeyword) || externParam.Modifiers.Any(SyntaxKind.RefKeyword)))
            {
                bool hasOut = externParam.Modifiers.Any(SyntaxKind.OutKeyword);
                arguments[paramIndex] = arguments[paramIndex].WithRefKindKeyword(TokenWithSpace(hasOut ? SyntaxKind.OutKeyword : SyntaxKind.RefKeyword));
            }
            else if (isOut && !isIn && !isReleaseMethod && parameterTypeInfo is PointerTypeHandleInfo { ElementType: HandleTypeHandleInfo pointedElementInfo } &&
                pointedElementInfo.Generator.TryGetHandleReleaseMethod(pointedElementInfo.Handle, paramAttributes, out string? outReleaseMethod) && !this.Reader.StringComparer.Equals(methodDefinition.Name, outReleaseMethod) &&
                (memorySize is null) && !isArray)
            {
                // NOTE: We don't handle scenarios where the parameter is [MemorySize] annotated (e.g. EnumProcessModules) or [NativeArrayInfo] (e.g. ITypeInfo.GetNames)
                if (this.RequestSafeHandle(outReleaseMethod) is TypeSyntax safeHandleType)
                {
                    signatureChanged = true;

                    IdentifierNameSyntax typeDefHandleName = IdentifierName(externParam.Identifier.ValueText + "Local");

                    // out SafeHandle
                    parameters[paramIndex] = externParam
                        .WithType(safeHandleType.WithTrailingTrivia(TriviaList(Space)))
                        .WithModifiers([TokenWithSpace(SyntaxKind.OutKeyword)]);

                    // HANDLE SomeLocal;
                    leadingStatements.Add(
                        LocalDeclarationStatement(
                            VariableDeclaration(
                                pointedElementInfo.ToTypeSyntax(parameterTypeSyntaxSettings, GeneratingElement.FriendlyOverload, null).Type,
                                [VariableDeclarator(typeDefHandleName.Identifier)])));

                    ArgumentSyntax ownsHandleArgument = Argument(
                        NameColon(IdentifierName("ownsHandle")),
                        refKindKeyword: default,
                        LiteralExpression(doNotRelease ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression));

                    if (this.canUseMarshalInitHandle)
                    {
                        // Some = new SafeHandle(default, ownsHandle: true);
                        leadingStatements.Add(
                            ExpressionStatement(
                                AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    origName,
                                    ObjectCreationExpression(safeHandleType, [Argument(LiteralExpression(SyntaxKind.DefaultLiteralExpression)), ownsHandleArgument]))));

                        // global::System.Runtime.InteropServices.Marshal.InitHandle(Some, SomeLocal);
                        trailingStatements.Add(
                            ExpressionStatement(
                                InvocationExpression(
                                    MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        ParseName($"global::{typeof(Marshal).FullName}"),
                                        IdentifierName("InitHandle")),
                                    [
                                        Argument(origName),
                                        Argument(this.GetIntPtrFromTypeDef(typeDefHandleName, pointedElementInfo)),
                                    ])));
                    }
                    else
                    {
                        // Some = new SafeHandle(SomeLocal, ownsHandle: true);
                        trailingStatements.Add(ExpressionStatement(AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            origName,
                            ObjectCreationExpression(safeHandleType, [Argument(this.GetIntPtrFromTypeDef(typeDefHandleName, pointedElementInfo)), ownsHandleArgument]))));
                    }

                    // Argument: &SomeLocal
                    arguments[paramIndex] = Argument(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, typeDefHandleName));
                }
            }
            else if (this.options.UseSafeHandles && isIn && !isOut && !isReleaseMethod && parameterTypeInfo is HandleTypeHandleInfo parameterHandleTypeInfo && this.TryGetHandleReleaseMethod(parameterHandleTypeInfo.Handle, paramAttributes, out string? releaseMethod) && !this.Reader.StringComparer.Equals(methodDefinition.Name, releaseMethod)
                && !(this.TryGetTypeDefFieldType(parameterHandleTypeInfo, out TypeHandleInfo? fieldType) && !this.IsSafeHandleCompatibleTypeDefFieldType(fieldType)))
            {
                IdentifierNameSyntax typeDefHandleName = IdentifierName(externParam.Identifier.ValueText + "Local");
                signatureChanged = true;

                IdentifierNameSyntax refAddedName = IdentifierName(externParam.Identifier.ValueText + "AddRef");

                // bool hParamNameAddRef = false;
                leadingOutsideTryStatements.Add(LocalDeclarationStatement(
                    VariableDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), [VariableDeclarator(refAddedName.Identifier, EqualsValueClause(LiteralExpression(SyntaxKind.FalseLiteralExpression)))])));

                // HANDLE hTemplateFileLocal;
                leadingStatements.Add(LocalDeclarationStatement(VariableDeclaration(externParam.Type, [VariableDeclarator(typeDefHandleName.Identifier)])));

                // throw new ArgumentNullException(nameof(hTemplateFile));
                StatementSyntax nullHandleStatement = ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentNullException))).WithArgumentList(ArgumentList(Argument(NameOfExpression(IdentifierName(externParam.Identifier.ValueText))))));
                if (isOptional)
                {
                    // (HANDLE)new IntPtr(-1);
                    HashSet<IntPtr> invalidValues = this.GetInvalidHandleValues(parameterHandleTypeInfo.Handle);
                    IntPtr invalidValue = invalidValues.Count > 0 ? GetPreferredInvalidHandleValue(invalidValues) : IntPtr.Zero;
                    ExpressionSyntax invalidExpression = CastExpression(externParam.Type, IntPtrExpr(invalidValue));

                    // hTemplateFileLocal = invalid-handle-value;
                    nullHandleStatement = ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, typeDefHandleName, invalidExpression));
                }

                // if (hTemplateFile is object)
                leadingStatements.Add(IfStatement(
                    BinaryExpression(SyntaxKind.IsExpression, origName, PredefinedType(Token(SyntaxKind.ObjectKeyword))),
                    Block(
                        //// hTemplateFile.DangerousAddRef(ref hTemplateFileAddRef);
                        ExpressionStatement(InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                origName,
                                IdentifierName(nameof(SafeHandle.DangerousAddRef))),
                            [Argument(refAddedName).WithRefKindKeyword(TokenWithSpace(SyntaxKind.RefKeyword))])),
                        //// hTemplateFileLocal = (HANDLE)hTemplateFile.DangerousGetHandle();
                        ExpressionStatement(
                            AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                typeDefHandleName,
                                CastExpression(
                                    externParam.Type.WithoutTrailingTrivia(),
                                    InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(SafeHandle.DangerousGetHandle))))))
                            .WithOperatorToken(TokenWithSpaces(SyntaxKind.EqualsToken)))),
                    //// else hTemplateFileLocal = default;
                    ElseClause(nullHandleStatement)));

                // if (hTemplateFileAddRef)
                //     hTemplateFile.DangerousRelease();
                finallyStatements.Add(
                    IfStatement(
                        refAddedName,
                        ExpressionStatement(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(SafeHandle.DangerousRelease))))))
                    .WithCloseParenToken(TokenWithLineFeed(SyntaxKind.CloseParenToken)));

                // Accept the SafeHandle instead.
                parameters[paramIndex] = externParam
                    .WithType(IdentifierName(nameof(SafeHandle)).WithTrailingTrivia(TriviaList(Space)));

                // hParamNameLocal;
                arguments[paramIndex] = Argument(typeDefHandleName);
            }
            else if ((externParam.Type is PointerTypeSyntax { ElementType: TypeSyntax ptrElementType }
                && (!IsVoid(ptrElementType) || (improvePointersToSpansAndRefs && isArray))
                && !this.IsInterface(parameterTypeInfo)) ||
                externParam.Type is ArrayTypeSyntax)
            {
                TypeSyntax elementType = externParam.Type is PointerTypeSyntax ptr ? ptr.ElementType
                    : externParam.Type is ArrayTypeSyntax array ? array.ElementType
                    : throw new InvalidOperationException();

                if (projectAsSpanBytes)
                {
                    elementType = PredefinedType(Token(SyntaxKind.ByteKeyword));
                }

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

                IdentifierNameSyntax localName = IdentifierName(origName + "Local");
                if (isArray)
                {
                    // TODO: add support for in/out size parameters. (e.g. RSGetViewports)
                    // TODO: add support for lists of pointers via a generated pointer-wrapping struct (e.g. PSSetSamplers)
                    if (!isPointerToPointer && TryHandleCountParam(elementType, nullableSource: true))
                    {
                        // If we used a Span, we might also want to generate a struct helper.
                        if (memorySize is not null && !isPointerToStructWithFlexibleArray)
                        {
                            countOfBytesStructParameters ??= new();
                            countOfBytesStructParameters.Add(param);
                        }
                    }
                    else if (countConst.HasValue && !isPointerToPointer && this.canUseSpan && externParam.Type is PointerTypeSyntax)
                    {
                        // TODO: add support for lists of pointers via a generated pointer-wrapping struct
                        signatureChanged = true;

                        // Accept a span instead of a pointer.
                        parameters[paramIndex] = parameters[paramIndex]
                            .WithType((isIn ? MakeReadOnlySpanOfT(elementType) : MakeSpanOfT(elementType)).WithTrailingTrivia(TriviaList(Space)));
                        fixedBlocks.Add(VariableDeclaration(externParam.Type, [VariableDeclarator(localName.Identifier, EqualsValueClause(origName))]));
                        arguments[paramIndex] = Argument(localName);

                        // Add a runtime check that the span is at least the required length.
                        leadingStatements.Add(IfStatement(
                            BinaryExpression(
                                SyntaxKind.LessThanExpression,
                                GetSpanLength(origName, false /* we've converted it to be a span */),
                                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(countConst.Value))),
                            ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentException))))));
                    }
                    else if (!isPointerToPointer && this.canUseSpan && externParam.Type is PointerTypeSyntax)
                    {
                        signatureChanged = true;

                        // Handle the byte* => Span<byte> mapping
                        parameters[paramIndex] = parameters[paramIndex]
                            .WithType((isConst ? MakeReadOnlySpanOfT(elementType) : MakeSpanOfT(elementType)).WithTrailingTrivia(TriviaList(Space)));
                        fixedBlocks.Add(VariableDeclaration(PointerType(elementType), [VariableDeclarator(localName.Identifier, EqualsValueClause(origName))]));
                        arguments[paramIndex] = projectAsSpanBytes ? Argument(CastExpression(externParam.Type, localName)) : Argument(localName);

                        if (projectAsSpanBytes)
                        {
                            numSpanByteParameters++;
                        }
                    }
                    else if (isNullTerminated && isConst && parameters[paramIndex].Type is PointerTypeSyntax { ElementType: PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.CharKeyword } } })
                    {
                        // replace char* with string
                        signatureChanged = true;
                        parameters[paramIndex] = parameters[paramIndex]
                            .WithType(PredefinedType(TokenWithSpace(SyntaxKind.StringKeyword)));
                        fixedBlocks.Add(VariableDeclaration(externParam.Type, [VariableDeclarator(localName.Identifier, EqualsValueClause(origName))]));
                        arguments[paramIndex] = Argument(localName);
                    }

                    // Translate ReadOnlySpan<PCWSTR> to ReadOnlySpan<string>
                    if (isIn && !isOut && isConst && externParam.Type is PointerTypeSyntax { ElementType: QualifiedNameSyntax { Right: { Identifier: { ValueText: "PCWSTR" } } } })
                    {
                        signatureChanged = true;

                        // Change the parameter type to ReadOnlySpan<string>
                        parameters[paramIndex] = externParam
                            .WithType(MakeReadOnlySpanOfT(PredefinedType(Token(SyntaxKind.StringKeyword))));

                        IdentifierNameSyntax gcHandlesLocal = IdentifierName($"{origName}GCHandles");
                        IdentifierNameSyntax pcwstrLocal = IdentifierName($"{origName}Pointers");

                        // var paramNameGCHandles = ArrayPool<GCHandle>.Shared.Rent(paramName.Length);
                        var gcHandlesArrayDecl = LocalDeclarationStatement(VariableDeclaration(
                            IdentifierName("var"),
                            [
                                VariableDeclarator(gcHandlesLocal.Identifier, EqualsValueClause(
                                    InvocationExpression(
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                ParseTypeName("global::System.Buffers.ArrayPool<global::System.Runtime.InteropServices.GCHandle>"),
                                                IdentifierName("Shared")),
                                            IdentifierName("Rent")),
                                        [Argument(GetSpanLength(origName, false))])))
                            ]));

                        // var paramNamePointers = ArrayPool<PCWSTR>.Shared.Rent(paramName.Length);
                        var strsArrayDecl = LocalDeclarationStatement(VariableDeclaration(
                            IdentifierName("var"),
                            [
                                VariableDeclarator(pcwstrLocal.Identifier, EqualsValueClause(
                                    InvocationExpression(
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                ParseTypeName($"global::System.Buffers.ArrayPool<{PCWSTRTypeSyntax.ToString()}>"),
                                                IdentifierName("Shared")),
                                            IdentifierName("Rent")),
                                        [Argument(GetSpanLength(origName, false))])))
                            ]));

                        // for (int i = 0; i < paramName.Length; i++)
                        // {
                        //     paramNameGCHandles[i] = GCHandle.Alloc(paramName[i], GCHandleType.Pinned);
                        //     paramNamePointers[i] = (char*)paramNameGCHandles[i].AddrOfPinnedObject();
                        // }
                        IdentifierNameSyntax loopVariable = IdentifierName("i");
                        var forLoop = ForStatement(
                            VariableDeclaration(
                                PredefinedType(Token(SyntaxKind.IntKeyword)),
                                [VariableDeclarator(loopVariable.Identifier, EqualsValueClause(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))))]),
                            BinaryExpression(SyntaxKind.LessThanExpression, loopVariable, GetSpanLength(origName, false)),
                            [PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, loopVariable)],
                            Block(
                                ExpressionStatement(AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    ElementAccessExpression(gcHandlesLocal, [Argument(loopVariable)]),
                                    InvocationExpression(
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            ParseTypeName("global::System.Runtime.InteropServices.GCHandle"),
                                            IdentifierName("Alloc")),
                                        [
                                            Argument(ElementAccessExpression(origName, [Argument(loopVariable)])),
                                            Argument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ParseTypeName("global::System.Runtime.InteropServices.GCHandleType"), IdentifierName("Pinned")))
                                        ]))),
                                ExpressionStatement(AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    ElementAccessExpression(pcwstrLocal, [Argument(loopVariable)]),
                                    CastExpression(
                                        PointerType(PredefinedType(Token(SyntaxKind.CharKeyword))),
                                        InvocationExpression(
                                            MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                ElementAccessExpression(gcHandlesLocal, [Argument(loopVariable)]),
                                                IdentifierName("AddrOfPinnedObject"))))))));

                        leadingOutsideTryStatements.AddRange([gcHandlesArrayDecl, strsArrayDecl, forLoop]);

                        // for (int i = 0; i < paramName.Length; i++)
                        // {
                        //     paramNameGCHandles[i].Free()
                        // }
                        var freeHandleStatement = ForStatement(
                            VariableDeclaration(
                                PredefinedType(Token(SyntaxKind.IntKeyword)),
                                [VariableDeclarator(loopVariable.Identifier, EqualsValueClause(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))))]),
                            BinaryExpression(SyntaxKind.LessThanExpression, loopVariable, GetSpanLength(origName, false)),
                            [PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, loopVariable)],
                            Block(
                                ExpressionStatement(
                                    InvocationExpression(
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            ElementAccessExpression(gcHandlesLocal, [Argument(loopVariable)]),
                                            IdentifierName("Free"))))));

                        // ArrayPool<GCHandle>.Shared.Return(gcHandlesArray);
                        var returnGCHandlesArray = ExpressionStatement(
                            InvocationExpression(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    ParseTypeName("global::System.Buffers.ArrayPool<global::System.Runtime.InteropServices.GCHandle>"),
                                    IdentifierName("Shared.Return")),
                                [Argument(gcHandlesLocal)]));

                        // ArrayPool<PCWSTR>.Shared.Return(paramNamePointers);
                        var returnStrsArray = ExpressionStatement(
                            InvocationExpression(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    ParseTypeName($"global::System.Buffers.ArrayPool<{PCWSTRTypeSyntax.ToString()}> "),
                                    IdentifierName("Shared.Return")),
                                [Argument(pcwstrLocal)]));

                        finallyStatements.AddRange([freeHandleStatement, returnGCHandlesArray, returnStrsArray]);

                        // Update fixed blocks already created to consume our array of pinned pointers
                        bool found = false;
                        for (int i = 0; i < fixedBlocks.Count; i++)
                        {
                            if (fixedBlocks[i] is VariableDeclarationSyntax { Variables: [VariableDeclaratorSyntax { Initializer: { Value: IdentifierNameSyntax { Identifier: SyntaxToken id } } initializer } variable] } declaration
                                && id.ValueText == externParam.Identifier.ValueText)
                            {
                                // fixed (PCWSTR* paramNamePointersPtr = strsArray)
                                fixedBlocks[i] = declaration.WithVariables([variable.WithInitializer(initializer.WithValue(pcwstrLocal))]);
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            throw new GenerationFailedException("Unable to find existing fixed block to change.");
                        }

                        arguments[paramIndex] = Argument(localName);
                    }
                }
                else if (isIn && isOptional && !isOut && !isPointerToPointer)
                {
                    signatureChanged = true;
                    parameters[paramIndex] = parameters[paramIndex]
                        .WithType(NullableType(elementType).WithTrailingTrivia(TriviaList(Space)));
                    leadingStatements.Add(
                        LocalDeclarationStatement(VariableDeclaration(
                            elementType,
                            [
                                VariableDeclarator(localName.Identifier, EqualsValueClause(
                                    BinaryExpression(SyntaxKind.CoalesceExpression, origName, DefaultExpression(elementType))))
                            ])));
                    arguments[paramIndex] = Argument(ConditionalExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName("HasValue")),
                        PrefixUnaryExpression(SyntaxKind.AddressOfExpression, localName),
                        LiteralExpression(SyntaxKind.NullLiteralExpression)));
                }
                else if (isIn && isOut)
                {
                    signatureChanged = true;
                    parameters[paramIndex] = parameters[paramIndex]
                        .WithType(elementType.WithTrailingTrivia(TriviaList(Space)))
                        .WithModifiers([TokenWithSpace(SyntaxKind.RefKeyword)]);
                    fixedBlocks.Add(VariableDeclaration(
                        externParam.Type,
                        [
                            VariableDeclarator(localName.Identifier, EqualsValueClause(
                                PrefixUnaryExpression(SyntaxKind.AddressOfExpression, origName)))
                        ]));
                    arguments[paramIndex] = Argument(localName);
                }
                else if (isOut && !isIn)
                {
                    signatureChanged = true;
                    parameters[paramIndex] = parameters[paramIndex]
                        .WithType(elementType.WithTrailingTrivia(TriviaList(Space)))
                        .WithModifiers([TokenWithSpace(SyntaxKind.OutKeyword)]);
                    fixedBlocks.Add(VariableDeclaration(
                        externParam.Type,
                        [VariableDeclarator(localName.Identifier, EqualsValueClause(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, origName)))]));
                    arguments[paramIndex] = Argument(localName);
                }
                else if (isIn && !isOut)
                {
                    // Use the "in" modifier to avoid copying the struct.
                    signatureChanged = true;
                    parameters[paramIndex] = parameters[paramIndex]
                        .WithType(elementType.WithTrailingTrivia(TriviaList(Space)))
                        .WithModifiers([TokenWithSpace(SyntaxKind.InKeyword)]);
                    fixedBlocks.Add(VariableDeclaration(
                        externParam.Type,
                        [VariableDeclarator(localName.Identifier, EqualsValueClause(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, origName)))]));
                    arguments[paramIndex] = Argument(localName);
                }
            }
            else if (isIn && !isOut && isConst && externParam.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PCWSTR" } } })
            {
                IdentifierNameSyntax localName = IdentifierName(origName + "Local");
                signatureChanged = true;
                parameters[paramIndex] = externParam
                    .WithType(PredefinedType(TokenWithSpace(SyntaxKind.StringKeyword)));
                fixedBlocks.Add(VariableDeclaration(
                    PointerType(PredefinedType(Token(SyntaxKind.CharKeyword))),
                    [VariableDeclarator(localName.Identifier, EqualsValueClause(origName))]));
                arguments[paramIndex] = Argument(localName);
            }
            else if (isIn && !isOut && isConst && externParam.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PCSTR" } } })
            {
                IdentifierNameSyntax localName = IdentifierName(origName + "Local");
                signatureChanged = true;
                parameters[paramIndex] = externParam
                    .WithType(PredefinedType(TokenWithSpace(SyntaxKind.StringKeyword)));

                // fixed (byte* someLocal = some is object ? System.Text.Encoding.Default.GetBytes(some) : null)
                fixedBlocks.Add(VariableDeclaration(
                    PointerType(PredefinedType(Token(SyntaxKind.ByteKeyword))),
                    [
                        VariableDeclarator(
                            localName.Identifier,
                            EqualsValueClause(
                                ConditionalExpression(
                                    BinaryExpression(SyntaxKind.IsExpression, origName, PredefinedType(Token(SyntaxKind.ObjectKeyword))),
                                    InvocationExpression(
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            ParseTypeName("global::System.Text.Encoding.Default"),
                                            IdentifierName(nameof(Encoding.GetBytes))),
                                        [Argument(origName)]),
                                    LiteralExpression(SyntaxKind.NullLiteralExpression))))
                    ]));

                // new PCSTR(someLocal)
                arguments[paramIndex] = Argument(ObjectCreationExpression(externParam.Type, [Argument(localName)]));
            }
            else if (isIn && isOut && this.canUseSpan && externParam.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PWSTR" } } })
            {
                IdentifierNameSyntax localName = IdentifierName("p" + origName);
                IdentifierNameSyntax localWstrName = IdentifierName("wstr" + origName);
                signatureChanged = true;
                parameters[paramIndex] = externParam
                    .WithType(MakeSpanOfT(PredefinedType(Token(SyntaxKind.CharKeyword))))
                    .AddModifiers(Token(SyntaxKind.RefKeyword));

                // fixed (char* pParam1 = Param1)
                fixedBlocks.Add(VariableDeclaration(PointerType(PredefinedType(Token(SyntaxKind.CharKeyword))), [VariableDeclarator(localName.Identifier, EqualsValueClause(origName))]));

                // wstrParam1
                arguments[paramIndex] = Argument(localWstrName);

                // if (buffer != null && buffer.LastIndexOf('\0') == -1) throw new ArgumentException("Required null terminator is missing.", "Param1");
                InvocationExpressionSyntax lastIndexOf = InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(MemoryExtensions.LastIndexOf))),
                    [Argument(LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal('\0')))]);
                ExpressionSyntax lastIndexOfEqualsMinusOne = BinaryExpression(SyntaxKind.EqualsExpression, lastIndexOf, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(-1)));
                ExpressionSyntax bufferNotNull = BinaryExpression(SyntaxKind.NotEqualsExpression, origName, LiteralExpression(SyntaxKind.NullLiteralExpression));
                leadingOutsideTryStatements.Add(IfStatement(
                    BinaryExpression(SyntaxKind.LogicalAndExpression, bufferNotNull, lastIndexOfEqualsMinusOne),
                    ThrowStatement(ObjectCreationExpression(
                        IdentifierName(nameof(ArgumentException)),
                        [
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("Required null terminator missing."))),
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(externParam.Identifier.ValueText)))
                        ]))));

                // PWSTR wstrParam1 = pParam1;
                leadingStatements.Add(LocalDeclarationStatement(
                    VariableDeclaration(externParam.Type, [VariableDeclarator(localWstrName.Identifier, EqualsValueClause(localName))])));

                // Param1 = Param1.Slice(0, wstrParam1.Length);
                trailingStatements.Add(ExpressionStatement(AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    origName,
                    InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(Span<>.Slice))),
                        [
                            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
                            Argument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, localWstrName, IdentifierName("Length")))
                        ]))));
            }
            else if (!isIn && isOut && this.canUseSpan && externParam.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PWSTR" } } })
            {
                IdentifierNameSyntax localName = IdentifierName(origName + "Local");
                signatureChanged = true;
                parameters[paramIndex] = externParam
                    .WithType(MakeSpanOfT(PredefinedType(Token(SyntaxKind.CharKeyword))));

                // fixed (char* pParam1 = Param1)
                fixedBlocks.Add(VariableDeclaration(PointerType(PredefinedType(Token(SyntaxKind.CharKeyword))), [VariableDeclarator(localName.Identifier, EqualsValueClause(origName))]));

                // Use the char* pointer as the argument instead of the parameter.
                arguments[paramIndex] = Argument(localName);

                // Remove the size parameter if one exists.
                TryHandleCountParam(PredefinedType(Token(SyntaxKind.CharKeyword)), nullableSource: false);
            }
            else if (isIn && isOptional && !isOut && isManagedParameterType && parameterTypeInfo is PointerTypeHandleInfo ptrInfo && ptrInfo.ElementType.IsValueType(parameterTypeSyntaxSettings) is true && this.canUseUnsafeAsRef)
            {
                // The extern method couldn't have exposed the parameter as a pointer because the type is managed.
                // It would have exposed as an `in` modifier, and non-optional. But we can expose as optional anyway.
                minorSignatureChange = true;
                IdentifierNameSyntax localName = IdentifierName(origName + "Local");
                parameters[paramIndex] = parameters[paramIndex]
                    .WithType(NullableType(externParam.Type).WithTrailingTrivia(TriviaList(Space)))
                    .WithModifiers(default); // drop the `in` modifier.
                leadingStatements.Add(
                    LocalDeclarationStatement(VariableDeclaration(
                        externParam.Type,
                        [VariableDeclarator(localName.Identifier, EqualsValueClause(BinaryExpression(SyntaxKind.CoalesceExpression, origName, DefaultExpression(externParam.Type))))])));

                // We can't pass in null, but we can be fancy to achieve the same effect.
                // Unsafe.NullRef<TParamType>() or Unsafe.AsRef<TParamType>(null), depending on what's available.
                ExpressionSyntax nullRef = this.canUseUnsafeNullRef
                    ? InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Unsafe)), GenericName("NullRef", [externParam.Type])))
                    : InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Unsafe)), GenericName(nameof(Unsafe.AsRef), [externParam.Type])),
                        [Argument(LiteralExpression(SyntaxKind.NullLiteralExpression))]);
                arguments[paramIndex] = Argument(ConditionalExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName("HasValue")),
                    RefExpression(localName),
                    RefExpression(nullRef)));
            }
            else if (this.options.AllowMarshaling && isOptional && isOut && !isArray && parameterTypeInfo is PointerTypeHandleInfo pointerTypeHandleInfo && this.IsInterface(pointerTypeHandleInfo.ElementType))
            {
                // In source generated COM we can improve certain Optional out parameters to marshalled, e.g. IWbemServices.GetObject has some
                // optional out parameters that need to be pointers in the ABI but in the optional overload they can be marshalled to ComWrappers.
                TypeSyntax interfaceTypeSyntax = pointerTypeHandleInfo.ElementType.ToTypeSyntax(parameterTypeSyntaxSettings, GeneratingElement.FriendlyOverload, null).Type;
                parameters[paramIndex] = parameters[paramIndex]
                    .WithType(interfaceTypeSyntax.WithTrailingTrivia(TriviaList(Space)))
                    .WithModifiers([TokenWithSpace(isIn && isOut ? SyntaxKind.RefKeyword : (isIn ? SyntaxKind.InKeyword : SyntaxKind.OutKeyword))]);

                TypeSyntax nativeInterfaceTypeSyntax = ((PointerTypeSyntax)externParam.Type).ElementType;

                if (!isIn)
                {
                    // Not a ref so need to assign first so we can use "ref" on the param. Use Unsafe.SkipInit(out origName) so that we can handle null refs.
                    leadingOutsideTryStatements.Add(
                        ExpressionStatement(
                            InvocationExpression(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName(nameof(Unsafe)),
                                    IdentifierName(nameof(Unsafe.SkipInit))),
                                [Argument(origName).WithRefKindKeyword(Token(SyntaxKind.OutKeyword))])));
                }

                // For both in & out, declare a local:
                // externParamTypeInterface* __origName_native = null;
                IdentifierNameSyntax nativeLocal = IdentifierName($"__{origName.Identifier.ValueText.Replace("@", string.Empty)}_native");
                leadingOutsideTryStatements.Add(
                    LocalDeclarationStatement(
                        VariableDeclaration(nativeInterfaceTypeSyntax, [VariableDeclarator(nativeLocal.Identifier, EqualsValueClause(LiteralExpression(SyntaxKind.NullLiteralExpression)))])));

                // bool __origName_present = !Unsafe.IsNullRef<TInterface>(origName);
                string paramPresent = $"__{origName.Identifier.ValueText.Replace("@", string.Empty)}_present";
                leadingOutsideTryStatements.Add(
                    LocalDeclarationStatement(VariableDeclaration(
                        PredefinedType(Token(SyntaxKind.BoolKeyword)),
                        [
                            VariableDeclarator(Identifier(paramPresent), EqualsValueClause(
                                PrefixUnaryExpression(
                                SyntaxKind.LogicalNotExpression,
                                InvocationExpression(
                                    MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        IdentifierName(nameof(Unsafe)),
                                        GenericName(nameof(Unsafe.IsNullRef), [interfaceTypeSyntax])),
                                    [Argument(RefExpression(origName))]))))
                        ])));

                // If it's an in parameter, assign the native local from the managed parameter.
                // __origName_native = (TNative)global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller<TInterface>.ConvertToUnmanaged(origName);
                // Also remember the marshalled in pointer in case the callee modifies in for ref params.
                // __origName_nativeIn = __origName_native;
                if (isIn)
                {
                    ExpressionSyntax toNativeExpression = this.useSourceGenerators ?
                        InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                GenericName($"global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller", [interfaceTypeSyntax]),
                                IdentifierName("ConvertToUnmanaged")),
                            [Argument(origName)]) :
                        ParenthesizedExpression(ConditionalExpression(
                            BinaryExpression(SyntaxKind.NotEqualsExpression, origName, LiteralExpression(SyntaxKind.NullLiteralExpression)),
                            CastExpression(
                                PointerType(PredefinedType(Token(SyntaxKind.VoidKeyword))),
                                InvocationExpression(
                                    MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        ParseTypeName($"global::System.Runtime.InteropServices.Marshal"),
                                        IdentifierName("GetIUnknownForObject")),
                                    [Argument(origName)])),
                            LiteralExpression(SyntaxKind.NullLiteralExpression)));

                    leadingStatements.Add(
                        IfStatement(
                            IdentifierName(paramPresent),
                            Block(
                                ExpressionStatement(
                                    AssignmentExpression(
                                        SyntaxKind.SimpleAssignmentExpression,
                                        nativeLocal,
                                        CastExpression(
                                            nativeInterfaceTypeSyntax,
                                            toNativeExpression))))));
                }

                // If it's an out parameter, assign the out parameter from the native local.
                // origName = global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller<TInterface>.ConvertToManaged(__origName_native);
                if (isOut)
                {
                    ExpressionSyntax toManagedExpression = this.useSourceGenerators ?
                        InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                GenericName($"global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller", [interfaceTypeSyntax]),
                                IdentifierName("ConvertToManaged")),
                            [Argument(nativeLocal)]) :
                        ParenthesizedExpression(ConditionalExpression(
                            BinaryExpression(SyntaxKind.NotEqualsExpression, nativeLocal, LiteralExpression(SyntaxKind.NullLiteralExpression)),
                            CastExpression(interfaceTypeSyntax, InvocationExpression(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    ParseTypeName($"global::System.Runtime.InteropServices.Marshal"),
                                    IdentifierName("GetObjectForIUnknown")),
                                [Argument(CastExpression(ParseName("nint"), nativeLocal))])),
                            LiteralExpression(SyntaxKind.NullLiteralExpression)));

                    trailingStatements.Add(
                        IfStatement(
                            IdentifierName(paramPresent),
                            Block(
                                ExpressionStatement(
                                    AssignmentExpression(
                                        SyntaxKind.SimpleAssignmentExpression,
                                        origName,
                                        toManagedExpression)))));
                }

                // Release the native pointers we have refs on.
                finallyStatements.Add(this.COMFreeNativePointerStatement(nativeLocal, interfaceTypeSyntax));

                // If it's an in parameter, pass the native local as the argument.
                arguments[paramIndex] = arguments[paramIndex].WithExpression(ConditionalExpression(IdentifierName(paramPresent), PrefixUnaryExpression(SyntaxKind.AddressOfExpression, nativeLocal), LiteralExpression(SyntaxKind.NullLiteralExpression)));
            }

            // Tag any [Optional] parameters that don't have modifiers as [Optional] so that C# can help callers omit them.
            if (isOptional && parameters[paramIndex].Modifiers.Count == 0)
            {
                parameters[paramIndex] = parameters[paramIndex].AddAttributeLists(AttributeList(OptionalAttributeSyntax));
            }

            bool TryHandleCountParam(TypeSyntax elementType, bool nullableSource)
            {
                IdentifierNameSyntax localName = IdentifierName(origName + "Local");

                // It is possible that countParamIndex points to a parameter that is not on the extern method
                // when the parameter is the last one and was moved to a return value.
                if (countParamIndex.HasValue
                    && this.canUseSpan
                    && externMethodDeclaration.ParameterList.Parameters.Count > countParamIndex.Value
                    && !(externMethodDeclaration.ParameterList.Parameters[countParamIndex.Value].Type is PointerTypeSyntax)
                    && !(externMethodDeclaration.ParameterList.Parameters[countParamIndex.Value].Modifiers.Any(SyntaxKind.OutKeyword) || externMethodDeclaration.ParameterList.Parameters[countParamIndex.Value].Modifiers.Any(SyntaxKind.RefKeyword)))
                {
                    signatureChanged = true;
                    bool remainsRefType = nullableSource;
                    if (externParam.Type is PointerTypeSyntax)
                    {
                        remainsRefType = false;
                        if (isCountOfBytes)
                        {
                            // For parameters annotated as count of bytes, we need to switch the friendly parameter to Span<byte>
                            // and then cast to (ParamType*) when we call the p/invoke.
                            TypeSyntax byteSyntax = PredefinedType(Token(SyntaxKind.ByteKeyword));
                            parameters[paramIndex] = parameters[paramIndex]
                                .WithType((!isOut ? MakeReadOnlySpanOfT(byteSyntax) : MakeSpanOfT(byteSyntax)).WithTrailingTrivia(TriviaList(Space)));
                            fixedBlocks.Add(VariableDeclaration(PointerType(byteSyntax), [VariableDeclarator(localName.Identifier, EqualsValueClause(origName))]));
                            arguments[paramIndex] = Argument(CastExpression(externParam.Type, localName));
                            numSpanByteParameters++;
                        }
                        else
                        {
                            parameters[paramIndex] = parameters[paramIndex]
                                .WithType((isIn ? MakeReadOnlySpanOfT(elementType) : MakeSpanOfT(elementType)).WithTrailingTrivia(TriviaList(Space)));
                            fixedBlocks.Add(VariableDeclaration(externParam.Type, [VariableDeclarator(localName.Identifier, EqualsValueClause(origName))]));
                            arguments[paramIndex] = Argument(localName);
                        }
                    }

                    ExpressionSyntax sizeArgExpression;
                    if (lengthParamUsedBy.TryGetValue(countParamIndex.Value, out int userIndex))
                    {
                        bool origNameIsRefType = remainsRefType;
                        bool otherUserNameIsRefType = parameters[userIndex].Type is ArrayTypeSyntax;

                        // Multiple array parameters share a common 'length' parameter.
                        // Since we're making this a little less obvious, add a quick if check in the helper method
                        // that enforces that all such parameters have a common span length.
                        ExpressionSyntax otherUserName = IdentifierName(parameters[userIndex].Identifier.ValueText);

                        // Only enforce length equality when both spans are non-empty.
                        ExpressionSyntax otherNotEmpty = PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, GetIsSpanEmpty(otherUserName, otherUserNameIsRefType));
                        ExpressionSyntax origNotEmpty = PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, GetIsSpanEmpty(origName, origNameIsRefType));
                        ExpressionSyntax lengthsNotEqual = BinaryExpression(
                            SyntaxKind.NotEqualsExpression,
                            GetSpanLength(otherUserName, otherUserNameIsRefType),
                            GetSpanLength(origName, origNameIsRefType));
                        ExpressionSyntax condition = BinaryExpression(SyntaxKind.LogicalAndExpression, BinaryExpression(SyntaxKind.LogicalAndExpression, otherNotEmpty, origNotEmpty), lengthsNotEqual);
                        leadingStatements.Add(IfStatement(
                            condition,
                            ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentException))))));

                        // Also we need to compound the size argument so that if one of the spans was empty, we pass the non-zero one.
                        sizeArgExpression = arguments[countParamIndex.Value].Expression;
                        if (sizeArgExpression is CastExpressionSyntax { Expression: ExpressionSyntax castedExpression })
                        {
                            // Unwrap the cast so we can simplify the logic
                            sizeArgExpression = castedExpression;
                        }

                        sizeArgExpression = ParenthesizedExpression(ConditionalExpression(GetIsSpanEmpty(origName, origNameIsRefType), sizeArgExpression, GetSpanLength(origName, origNameIsRefType)));
                    }
                    else
                    {
                        lengthParamUsedBy.Add(countParamIndex.Value, paramIndex);

                        sizeArgExpression = GetSpanLength(origName, remainsRefType);
                    }

                    // Always wrap the sizeArgExpression in CastExpression if needed.
                    if (!(parameters[countParamIndex.Value].Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.IntKeyword } }))
                    {
                        sizeArgExpression = CastExpression(parameters[countParamIndex.Value].Type!, sizeArgExpression);
                    }

                    arguments[countParamIndex.Value] = Argument(sizeArgExpression);

                    return true;
                }

                return false;
            }
        }

        if (emulateMemberFunctionCallConv)
        {
            // Turn the __retVal parameter into a local with default.
            SyntaxToken retValLocalName = ((IdentifierNameSyntax)arguments[0].Expression).Identifier;

            // Return type of the friendly method is the non-pointer struct return.
            externMethodReturnType = ((PointerTypeSyntax)externMethodReturnType).ElementType;
            LocalDeclarationStatementSyntax localRetValDecl = LocalDeclarationStatement(
                VariableDeclaration(externMethodReturnType, [VariableDeclarator(retValLocalName, EqualsValueClause(DefaultExpression(externMethodReturnType)))]));
            leadingStatements.Add(localRetValDecl);

            // Pass in the local as the return value.
            arguments[0] = Argument(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, IdentifierName(retValLocalName)));
            parametersToRemove.Add(0);

            friendlyMethodName = Identifier(friendlyMethodName.ValueText.Replace(EmulateMemberFunctionCallConvSuffix, string.Empty));
            signatureChanged = true;
        }

        TypeSyntax? returnSafeHandleType = originalSignature.ReturnType is HandleTypeHandleInfo returnTypeHandleInfo
            && returnTypeHandleInfo.Generator.TryGetHandleReleaseMethod(returnTypeHandleInfo.Handle, returnTypeAttributes, out string? returnReleaseMethod)
            ? this.RequestSafeHandle(returnReleaseMethod) : null;

        IdentifierNameSyntax resultLocal = IdentifierName("__result");

        if (this.canUseMarshalInitHandle && returnSafeHandleType is not null)
        {
            IdentifierNameSyntax resultSafeHandleLocal = IdentifierName("__resultSafeHandle");

            // SafeHandle __resultSafeHandle = new SafeHandle(default, ownsHandle: true);
            leadingStatements.Add(
                LocalDeclarationStatement(
                    VariableDeclaration(
                        returnSafeHandleType,
                        [
                            VariableDeclarator(
                                resultSafeHandleLocal.Identifier,
                                EqualsValueClause(
                                    ObjectCreationExpression(
                                        returnSafeHandleType,
                                        [
                                            Argument(LiteralExpression(SyntaxKind.DefaultLiteralExpression)),
                                            Argument(
                                                NameColon(IdentifierName("ownsHandle")),
                                                refKindKeyword: default,
                                                LiteralExpression(doNotRelease ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression))
                                        ])))
                        ])));

            // global::System.Runtime.InteropServices.Marshal.InitHandle(__resultSafeHandle, __result);
            trailingStatements.Add(
                ExpressionStatement(
                    InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            ParseName($"global::{typeof(Marshal).FullName}"),
                            IdentifierName("InitHandle")),
                        [
                            Argument(resultSafeHandleLocal),
                            Argument(this.GetIntPtrFromTypeDef(resultLocal, originalSignature.ReturnType)),
                        ])));
        }

        if ((returnSafeHandleType is not null || minorSignatureChange) && !signatureChanged)
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
                DocumentationCommentTrivia(
                    SyntaxKind.SingleLineDocumentationCommentTrivia,
                    [
                        XmlText($"/// "),
                        XmlEmptyElement("inheritdoc", [XmlCrefAttribute(NameMemberCref(docRefExternName, ToCref(externMethodDeclaration.ParameterList)))]),
                        XmlText(XmlTextNewLine("\n", continueXmlDocumentationComment: false))
                    ]));
            ExpressionSyntax externInvocation = InvocationExpression(
                overloadOf switch
                {
                    FriendlyOverloadOf.ExternMethod => QualifiedName(declaringTypeName, IdentifierName(externMethodDeclaration.Identifier.Text)),
                    FriendlyOverloadOf.StructMethod => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName(externMethodDeclaration.Identifier.Text)),
                    FriendlyOverloadOf.InterfaceMethod => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("@this"), IdentifierName(externMethodDeclaration.Identifier.Text)),
                    _ => throw new NotSupportedException("Unrecognized friendly overload mode " + overloadOf),
                },
                [.. arguments]);
            bool hasVoidReturn = externMethodReturnType is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.VoidKeyword } };
            BlockSyntax? body = Block([.. leadingStatements]);
            if (returnSafeHandleType is not null)
            {
                // HANDLE result = invocation();
                body = body.AddStatements(LocalDeclarationStatement(VariableDeclaration(externMethodReturnType, [VariableDeclarator(resultLocal.Identifier, EqualsValueClause(externInvocation))])));

                body = body.AddStatements(trailingStatements.ToArray());

                ReturnStatementSyntax returnStatement;
                if (this.canUseMarshalInitHandle)
                {
                    // return __resultSafeHandle;
                    returnStatement = ReturnStatement(IdentifierName("__resultSafeHandle"));
                }
                else
                {
                    // return new SafeHandle(result, ownsHandle: true);
                    returnStatement = ReturnStatement(ObjectCreationExpression(
                        returnSafeHandleType,
                        [
                            Argument(this.GetIntPtrFromTypeDef(resultLocal, originalSignature.ReturnType)),
                            Argument(
                                NameColon(IdentifierName("ownsHandle")),
                                refKindKeyword: default,
                                LiteralExpression(doNotRelease ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression))
                        ]));
                }

                body = body.AddStatements(returnStatement);
            }
            else if (hasVoidReturn)
            {
                body = body.AddStatements(ExpressionStatement(externInvocation));
                body = body.AddStatements(trailingStatements.ToArray());
            }
            else
            {
                if (emulateMemberFunctionCallConv)
                {
                    externInvocation = PrefixUnaryExpression(SyntaxKind.PointerIndirectionExpression, externInvocation);
                }

                // var result = externInvocation();
                body = body.AddStatements(LocalDeclarationStatement(VariableDeclaration(externMethodReturnType, [VariableDeclarator(resultLocal.Identifier, EqualsValueClause(externInvocation))])));

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
                body = Block(
                [
                    .. leadingOutsideTryStatements,
                    TryStatement(body, default, FinallyClause(Block([.. finallyStatements])))
                ]);
            }
            else if (leadingOutsideTryStatements.Count > 0)
            {
                body = body.WithStatements(body.Statements.InsertRange(0, leadingOutsideTryStatements));
            }

            SyntaxTokenList modifiers = [TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword)];
            if (overloadOf != FriendlyOverloadOf.StructMethod)
            {
                modifiers = modifiers.Insert(1, TokenWithSpace(SyntaxKind.StaticKeyword));
            }

            if (overloadOf == FriendlyOverloadOf.InterfaceMethod)
            {
                parameters.Insert(0, Parameter(declaringTypeName.WithTrailingTrivia(TriviaList(Space)), Identifier("@this")).AddModifiers(TokenWithSpace(SyntaxKind.ThisKeyword)));
            }

            body = body
                .WithOpenBraceToken(Token(TriviaList(LineFeed), SyntaxKind.OpenBraceToken, TriviaList(LineFeed)))
                .WithCloseBraceToken(TokenWithLineFeed(SyntaxKind.CloseBraceToken));

            MethodDeclarationSyntax friendlyDeclaration = externMethodDeclaration
                .WithReturnType(externMethodReturnType.WithTrailingTrivia(TriviaList(Space)))
                .WithIdentifier(friendlyMethodName)
                .WithModifiers(modifiers)
                .WithAttributeLists(default)
                .WithParameterList(FixTrivia(ParameterList([.. parameters])))
                .WithBody(body)
                .WithSemicolonToken(default);

            if (returnSafeHandleType is object)
            {
                friendlyDeclaration = friendlyDeclaration.WithReturnType(returnSafeHandleType.WithTrailingTrivia(TriviaList(Space)));
            }

            if (this.GetSupportedOSPlatformAttribute(methodDefinition.GetCustomAttributes()) is AttributeSyntax supportedOSPlatformAttribute)
            {
                friendlyDeclaration = friendlyDeclaration.AddAttributeLists(AttributeList(supportedOSPlatformAttribute));
            }

            // If we're using C# 13 or later, consider adding the overload resolution attribute if it would likely resolve ambiguities.
            if (this.LanguageVersion >= (LanguageVersion)1300 && parameters.Count == externMethodDeclaration.ParameterList.Parameters.Count)
            {
                this.volatileCode.GenerationTransaction(() => this.DeclareOverloadResolutionPriorityAttributeIfNecessary());
                friendlyDeclaration = friendlyDeclaration.AddAttributeLists(AttributeList(OverloadResolutionPriorityAttribute(1)));
            }

            friendlyDeclaration = friendlyDeclaration
                .WithLeadingTrivia(leadingTrivia);

            if (bookkeeping is not null)
            {
                bookkeeping.NumSpanByteParameters = numSpanByteParameters;
            }

            yield return friendlyDeclaration;

            // We generated the main overload, but now see if we should generate another helper for things like SHGetFileInfo where
            // there is a parameter that's sized in bytes and for convenience you want to just use the struct and not cast between Span<byte>.
            // To avoid an explosion of overloads, just do this if there's one parameter of this kind.
            if (improvePointersToSpansAndRefs && countOfBytesStructParameters?.Count == 1)
            {
                MethodDeclarationSyntax? structOverload = this.DeclareStructCountOfBytesFriendlyOverload(externMethodDeclaration, countOfBytesStructParameters, friendlyDeclaration, emulateMemberFunctionCallConv);
                if (structOverload is not null)
                {
                    yield return structOverload;
                }
            }
        }

        if (numOptionalParams > 0 && !omitOptionalParams && improvePointersToSpansAndRefs)
        {
            // Generate overloads for optional parameters.
            foreach (MethodDeclarationSyntax method in this.DeclareFriendlyOverload(methodDefinition, externMethodDeclaration, declaringTypeName, overloadOf, helperMethodsAdded, avoidWinmdRootAlias, improvePointersToSpansAndRefs, omitOptionalParams: true))
            {
                yield return method;
            }
        }
    }

    private MethodDeclarationSyntax? DeclareStructCountOfBytesFriendlyOverload(MethodDeclarationSyntax externMethodDeclaration, List<Parameter> countOfBytesStructParameters, MethodDeclarationSyntax friendlyDeclaration, bool emulateMemberFunctionCallConv)
    {
        // Can't easily generate the helpers we want to on net472, so just bail out if the ref helpers aren't present.
        if (!this.canCallCreateSpan)
        {
            return null;
        }

        // Swap the parameter that is Span<byte> typed for one that is struct-typed and generate this helper:
        //   internal static unsafe winmdroot.Foundation.BOOL InitializeAcl(out winmdroot.Security.ACL pAcl, winmdroot.Security.ACE_REVISION dwAclRevision)
        //   {
        //     pAcl = default;
        //     return InitializeAcl(MemoryMarshal.AsBytes(new Span<winmdroot.Security.ACL>(ref pAcl)), dwAclRevision);
        //   }
        // If it's an "out" parameter then notice we need to put a local that assigns to default before we call new Span on it.
        // If it's an "in" parameter then use ReadOnlySpan like this:
        //   return InitializeAcl(MemoryMarshal.AsBytes(new ReadOnlySpan<winmdroot.Security.ACL>(in pAcl)), dwAclRevision);
        // And if it's in & out, then use Ref with Span.
        List<ParameterSyntax> externParams = externMethodDeclaration.ParameterList.Parameters.Select(StripAttributes).ToList();
        Parameter param = countOfBytesStructParameters[0];
        ParameterSyntax externParam = externParams[param.SequenceNumber - 1 + (emulateMemberFunctionCallConv ? 1 : 0)];
        ParameterSyntax[] friendlyParams = friendlyDeclaration.ParameterList.Parameters.ToArray();
        int friendlyParamIndex = Array.FindIndex(friendlyParams, x => x.Identifier.Text == externParam.Identifier.Text);

        CustomAttributeHandleCollection paramAttributes = param.GetCustomAttributes();
        bool isIn = (param.Attributes & ParameterAttributes.In) == ParameterAttributes.In;
        bool isConst = this.FindInteropDecorativeAttribute(paramAttributes, "ConstAttribute") is not null;
        bool isComOutPtr = this.FindInteropDecorativeAttribute(paramAttributes, "ComOutPtrAttribute") is not null;
        bool isOut = isComOutPtr || (param.Attributes & ParameterAttributes.Out) == ParameterAttributes.Out;

        // Only proceed if the existing friendly overload replaced a pointer to a struct (or similar) with Span<byte>/ReadOnlySpan<byte>.
        if (externParam.Type is not PointerTypeSyntax pts || pts.ElementType is PredefinedTypeSyntax || IsVoidPtrOrPtrPtr(externParam.Type))
        {
            return null;
        }

        TypeSyntax structType = pts.ElementType.WithTrailingTrivia(TriviaList(Space));

        // Build the new parameter (in/out/ref struct)
        SyntaxToken paramName = externParam.Identifier;
        ParameterSyntax newParam = Parameter(structType, paramName);

        if (isIn && isOut)
        {
            newParam = newParam.WithModifiers([TokenWithSpace(SyntaxKind.RefKeyword)]);
        }
        else if (isOut && !isIn)
        {
            newParam = newParam.WithModifiers([TokenWithSpace(SyntaxKind.OutKeyword)]);
        }
        else if (isIn && !isOut)
        {
            // Honor const (already implies in)
            newParam = newParam.WithModifiers([TokenWithSpace(SyntaxKind.InKeyword)]);
        }

        // Construct the argument list for invoking the Span<byte> overload.
        // Start from the original extern signature since the friendly Span<byte> overload keeps parameter names.
        var invocationArguments = friendlyParams
            .Select(p => Argument(IdentifierName(p.Identifier.Text))
                .WithRefKindKeyword(p.Modifiers.FirstOrDefault(m => m.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword or SyntaxKind.InKeyword)))
            .ToArray();

        // Build the MemoryMarshal.AsBytes(...) expression that replaces this struct parameter.
        // Choose Span<> or ReadOnlySpan<> and ref kind inside the span constructor.
        bool spanIsReadOnly = isIn && !isOut;
        TypeSyntax spanType = spanIsReadOnly ? MakeReadOnlySpanOfT(structType.WithoutTrailingTrivia()) : MakeSpanOfT(structType.WithoutTrailingTrivia());
        SyntaxKind refKindForSpanCtor = spanIsReadOnly ? SyntaxKind.InKeyword : SyntaxKind.RefKeyword;

        ObjectCreationExpressionSyntax spanCreation = ObjectCreationExpression(spanType, [Argument(IdentifierName(paramName)).WithRefKindKeyword(Token(refKindForSpanCtor))]);

        ExpressionSyntax memoryMarshalAsBytes = InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                ParseName("global::System.Runtime.InteropServices.MemoryMarshal"),
                IdentifierName("AsBytes")),
            [Argument(spanCreation)]);

        invocationArguments[friendlyParamIndex] = Argument(memoryMarshalAsBytes);

        // The helper should invoke the existing overload by name (same identifier) which now expects Span<byte>/ReadOnlySpan<byte>.
        InvocationExpressionSyntax call = InvocationExpression(IdentifierName(friendlyDeclaration.Identifier), [.. invocationArguments]);

        // Build method modifiers.
        var modifiers = friendlyDeclaration.Modifiers;

        // Replace the parameter in our list.
        friendlyParams[friendlyParamIndex] = newParam;

        // Build body statements.
        var statements = new List<StatementSyntax>();
        if (isOut && !isIn)
        {
            // pAcl = default;
            statements.Add(ExpressionStatement(
                AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    IdentifierName(paramName),
                    DefaultExpression(structType.WithoutTrailingTrivia()))));
        }

        // return <call>;
        if (friendlyDeclaration.ReturnType is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.VoidKeyword } })
        {
            statements.Add(ExpressionStatement(call));
        }
        else
        {
            statements.Add(ReturnStatement(call));
        }

        BlockSyntax body = Block([.. statements])
            .WithOpenBraceToken(Token(TriviaList(LineFeed), SyntaxKind.OpenBraceToken, TriviaList(LineFeed)))
            .WithCloseBraceToken(TokenWithLineFeed(SyntaxKind.CloseBraceToken));

        // Doc comment inherit from extern.
        SyntaxTriviaList leadingTrivia = friendlyDeclaration.GetLeadingTrivia();

        MethodDeclarationSyntax helper = MethodDeclaration(
                friendlyDeclaration.ReturnType.WithTrailingTrivia(TriviaList(Space)),
                friendlyDeclaration.Identifier)
            .WithAttributeLists(friendlyDeclaration.AttributeLists)
            .WithModifiers(modifiers)
            .WithParameterList(FixTrivia(ParameterList([.. friendlyParams])))
            .WithBody(body)
            .WithSemicolonToken(default)
            .WithLeadingTrivia(leadingTrivia);

        return helper;
    }

    private StatementSyntax COMFreeNativePointerStatement(ExpressionSyntax nativePointer, TypeSyntax interfaceTypeSyntax)
    {
        if (this.useSourceGenerators)
        {
            // Release the nativeLocal via ComInterfaceMarshaller.Free.
            return
                ExpressionStatement(
                    InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            GenericName($"global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller", [interfaceTypeSyntax]),
                            IdentifierName("Free")),
                        [Argument(nativePointer)]));
        }
        else
        {
            // Finally, release the nativeLocal via Marshal.Release.
            return
                IfStatement(
                    BinaryExpression(SyntaxKind.NotEqualsExpression, nativePointer, LiteralExpression(SyntaxKind.NullLiteralExpression)),
                    ExpressionStatement(
                        InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                ParseTypeName("global::System.Runtime.InteropServices.Marshal"),
                                IdentifierName("Release")),
                            [Argument(CastExpression(ParseName("nint"), nativePointer))])))
                .WithCloseParenToken(TokenWithLineFeed(SyntaxKind.CloseParenToken));
        }
    }

    private class FriendlyMethodBookkeeping
    {
        public int NumSpanByteParameters { get; set; } = 0;
    }
}
