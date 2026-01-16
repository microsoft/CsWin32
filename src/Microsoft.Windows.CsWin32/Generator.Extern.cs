// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    /// <inheritdoc/>
    public void GenerateAllExternMethods(CancellationToken cancellationToken)
    {
        var initialOrder = this.MetadataIndex.Apis.SelectMany(api => this.Reader.GetTypeDefinition(api).GetMethods());
        var sortedMethods = initialOrder
            .OrderBy(methodHandle => this.Reader.GetString(this.Reader.GetMethodDefinition(methodHandle).Name));

        foreach (MethodDefinitionHandle methodHandle in sortedMethods)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MethodDefinition methodDef = this.Reader.GetMethodDefinition(methodHandle);
            if (this.IsCompatibleWithPlatform(methodDef.GetCustomAttributes()))
            {
                try
                {
                    this.volatileCode.GenerationTransaction(delegate
                    {
                        this.RequestExternMethod(methodHandle);
                    });
                }
                catch (GenerationFailedException ex) when (IsPlatformCompatibleException(ex))
                {
                    // Something transitively required for this method is not available for this platform, so skip this method.
                }
            }
        }
    }

    /// <summary>
    /// Generates all extern methods exported from a particular module, along with all their supporting types.
    /// </summary>
    /// <param name="moduleName">The name of the module for whose exports extern methods should be generated for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> if a matching module name was found and extern methods generated; otherwise <see langword="false"/>.</returns>
    public bool TryGenerateAllExternMethods(string moduleName, CancellationToken cancellationToken)
    {
        bool successful = false;
        foreach (MethodDefinitionHandle methodHandle in this.MetadataIndex.Apis.SelectMany(api => this.Reader.GetTypeDefinition(api).GetMethods()))
        {
            cancellationToken.ThrowIfCancellationRequested();

            MethodDefinition methodDef = this.Reader.GetMethodDefinition(methodHandle);
            ModuleReferenceHandle moduleHandle = methodDef.GetImport().Module;
            if (moduleHandle.IsNil)
            {
                continue;
            }

            ModuleReference module = this.Reader.GetModuleReference(moduleHandle);
            if (this.Reader.StringComparer.Equals(module.Name, moduleName + ".dll", ignoreCase: true))
            {
                string? bannedReason = null;
                foreach (KeyValuePair<string, string> bannedApi in this.BannedAPIs)
                {
                    if (this.Reader.StringComparer.Equals(methodDef.Name, bannedApi.Key))
                    {
                        // Skip a banned API.
                        bannedReason = bannedApi.Value;
                        continue;
                    }
                }

                if (bannedReason is object)
                {
                    continue;
                }

                if (this.IsCompatibleWithPlatform(methodDef.GetCustomAttributes()))
                {
                    try
                    {
                        this.volatileCode.GenerationTransaction(delegate
                        {
                            this.RequestExternMethod(methodHandle);
                        });
                    }
                    catch (GenerationFailedException ex) when (IsPlatformCompatibleException(ex))
                    {
                        // Something transitively required for this method is not available for this platform, so skip this method.
                    }
                }

                successful = true;
            }
        }

        return successful;
    }

    /// <inheritdoc/>
    public bool TryGenerateExternMethod(string possiblyQualifiedName, out IReadOnlyCollection<string> preciseApi)
    {
        if (possiblyQualifiedName is null)
        {
            throw new ArgumentNullException(nameof(possiblyQualifiedName));
        }

        if (this.GetMethodByName(possiblyQualifiedName) is MethodDefinitionHandle methodDefHandle)
        {
            MethodDefinition methodDef = this.Reader.GetMethodDefinition(methodDefHandle);
            string methodName = this.Reader.StringComparer.Equals(methodDef.Name, possiblyQualifiedName) ? possiblyQualifiedName : this.Reader.GetString(methodDef.Name);
            if (this.BannedAPIs.TryGetValue(methodName, out string? reason))
            {
                throw new NotSupportedException(reason);
            }

            this.volatileCode.GenerationTransaction(delegate
            {
                this.RequestExternMethod(methodDefHandle);
            });

            string methodNamespace = this.GetMethodNamespace(methodDef);
            preciseApi = ImmutableList.Create($"{methodNamespace}.{methodName}");
            return true;
        }

        preciseApi = ImmutableList<string>.Empty;
        return false;
    }

    internal void RequestExternMethod(MethodDefinitionHandle methodDefinitionHandle)
    {
        if (methodDefinitionHandle.IsNil)
        {
            return;
        }

        MethodDefinition methodDefinition = this.Reader.GetMethodDefinition(methodDefinitionHandle);
        if (!this.IsCompatibleWithPlatform(methodDefinition.GetCustomAttributes()))
        {
            // We've been asked for an interop type that does not apply. This happens because the metadata
            // may use a TypeReferenceHandle or TypeDefinitionHandle to just one of many arch-specific definitions of this type.
            // Try to find the appropriate definition for our target architecture.
            TypeDefinition declaringTypeDef = this.Reader.GetTypeDefinition(methodDefinition.GetDeclaringType());
            string ns = this.Reader.GetString(declaringTypeDef.Namespace);
            string methodName = this.Reader.GetString(methodDefinition.Name);
            if (this.MetadataIndex.MetadataByNamespace[ns].MethodsForOtherPlatform.Contains(methodName))
            {
                throw new PlatformIncompatibleException($"Request for method ({methodName}) that is not available given the target platform.");
            }
        }

        this.volatileCode.GenerateMethod(methodDefinitionHandle, () => this.DeclareExternMethod(methodDefinitionHandle));
    }

    private string GetMethodNamespace(MethodDefinition methodDef) => this.Reader.GetString(this.Reader.GetTypeDefinition(methodDef.GetDeclaringType()).Namespace);

    private void DeclareExternMethod(MethodDefinitionHandle methodDefinitionHandle)
    {
        MethodDefinition methodDefinition = this.Reader.GetMethodDefinition(methodDefinitionHandle);
        MethodImport import = methodDefinition.GetImport();
        if (import.Name.IsNil)
        {
            // Not an exported method.
            return;
        }

        string? methodName = this.Reader.GetString(methodDefinition.Name);
        try
        {
            if (this.WideCharOnly && IsAnsiFunction(methodName))
            {
                // Skip Ansi functions.
                return;
            }

            string? moduleName = this.GetNormalizedModuleName(import);

            string? entrypoint = null;
            if (this.TryGetRenamedMethod(methodName, out string? newName))
            {
                entrypoint = methodName;
                methodName = newName;
            }

            if (!import.Name.IsNil && import.Name != methodDefinition.Name)
            {
                entrypoint = this.Reader.GetString(import.Name);
            }

            // If this method releases a handle, recreate the method signature such that we take the struct rather than the SafeHandle as a parameter.
            TypeSyntaxSettings typeSettings = this.MetadataIndex.ReleaseMethods.Contains(entrypoint ?? methodName) ? this.externReleaseSignatureTypeSettings : this.externSignatureTypeSettings;
            MethodSignature<TypeHandleInfo> signature = methodDefinition.DecodeSignature(this.SignatureHandleProvider, null);
            bool requiresUnicodeCharSet = signature.ParameterTypes.Any(p => p is PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.Char });

            CustomAttributeHandleCollection? returnTypeAttributes = this.GetReturnTypeCustomAttributes(methodDefinition);
            TypeSyntaxAndMarshaling returnType = signature.ReturnType.ToTypeSyntax(typeSettings with { IsReturnValue = true }, GeneratingElement.ExternMethod, returnTypeAttributes?.QualifyWith(this), ParameterAttributes.Out);

            bool customMarshaling = this.options.AllowMarshaling && this.useSourceGenerators;

            // Search for any enum substitutions.
            TypeSyntax? returnTypeEnumName = customMarshaling ? null : this.FindAssociatedEnum(returnTypeAttributes);
            TypeSyntax?[]? parameterEnumType = null;
            foreach (ParameterHandle parameterHandle in methodDefinition.GetParameters())
            {
                Parameter parameter = this.Reader.GetParameter(parameterHandle);
                if (parameter.SequenceNumber == 0)
                {
                    continue;
                }

                if (this.FindAssociatedEnum(parameter.GetCustomAttributes()) is IdentifierNameSyntax parameterEnumName && !customMarshaling)
                {
                    parameterEnumType ??= new TypeSyntax?[signature.ParameterTypes.Length];
                    parameterEnumType[parameter.SequenceNumber - 1] = parameterEnumName;
                }
            }

            bool setLastError = (import.Attributes & MethodImportAttributes.SetLastError) == MethodImportAttributes.SetLastError;
            bool setLastErrorViaMarshaling = setLastError && (this.Options.AllowMarshaling || !this.canUseSetLastPInvokeError);
            bool setLastErrorManually = setLastError && !setLastErrorViaMarshaling;
            bool useGenerator = this.useSourceGenerators;

            AttributeListSyntax CreateDllImportAttributeList()
            {
                AttributeListSyntax result = AttributeList(
                    useGenerator ?
                        LibraryImport(import, moduleName, entrypoint, setLastErrorViaMarshaling, requiresUnicodeCharSet ? CharSet.Unicode : CharSet.Ansi) :
                        DllImport(import, moduleName, entrypoint, setLastErrorViaMarshaling, requiresUnicodeCharSet ? CharSet.Unicode : CharSet.Ansi))
                    .WithCloseBracketToken(TokenWithLineFeed(SyntaxKind.CloseBracketToken));
                if (this.generateDefaultDllImportSearchPathsAttribute)
                {
                    result = result.AddAttributes(
                        this.AppLocalLibraries.Contains(moduleName)
                            ? DefaultDllImportSearchPathsAllowAppDirAttribute
                            : DefaultDllImportSearchPathsAttribute);
                }

                if (useGenerator && requiresUnicodeCharSet)
                {
                    this.DeclareCharSetWorkaroundIfNecessary();
                }

                return result;
            }

            MethodDeclarationSyntax externDeclaration = MethodDeclaration(
                [CreateDllImportAttributeList()],
                modifiers: [TokenWithSpace(SyntaxKind.StaticKeyword)],
                returnType.Type.WithTrailingTrivia(TriviaList(Space)),
                explicitInterfaceSpecifier: null!,
                SafeIdentifier(methodName),
                null!,
                this.CreateParameterList(methodDefinition, signature, typeSettings, GeneratingElement.ExternMethod),
                default,
                body: null!,
                TokenWithLineFeed(SyntaxKind.SemicolonToken));
            externDeclaration = returnType.AddReturnMarshalAs(externDeclaration);

            if (!useGenerator)
            {
                externDeclaration = externDeclaration.AddModifiers(TokenWithSpace(SyntaxKind.ExternKeyword));
            }

            bool requiresUnsafe = RequiresUnsafe(externDeclaration.ReturnType) || externDeclaration.ParameterList.Parameters.Any(p => RequiresUnsafe(p.Type));
            if (requiresUnsafe)
            {
                externDeclaration = externDeclaration.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
            }

            MethodDeclarationSyntax exposedMethod;
            if (returnTypeEnumName is null && parameterEnumType is null && !setLastErrorManually)
            {
                // No need for wrapping the extern method, so just expose it directly.
                exposedMethod = externDeclaration.WithModifiers(externDeclaration.Modifiers.Insert(0, TokenWithSpace(this.Visibility)));
            }
            else
            {
                string ns = this.GetMethodNamespace(methodDefinition);
                NameSyntax nsSyntax = ParseName(ReplaceCommonNamespaceWithAlias(this, ns));
                ParameterListSyntax exposedParameterList = this.CreateParameterList(methodDefinition, signature, typeSettings, GeneratingElement.ExternMethod);
                static SyntaxToken RefInOutKeyword(ParameterSyntax p) =>
                    p.Modifiers.Any(SyntaxKind.OutKeyword) ? TokenWithSpace(SyntaxKind.OutKeyword) :
                    p.Modifiers.Any(SyntaxKind.RefKeyword) ? TokenWithSpace(SyntaxKind.RefKeyword) :
                    default;
                ArgumentListSyntax argumentList = ArgumentList([.. exposedParameterList.Parameters.Select(p => Argument(IdentifierName(p.Identifier.ValueText)).WithRefKindKeyword(RefInOutKeyword(p)))]);
                if (parameterEnumType is not null)
                {
                    for (int i = 0; i < parameterEnumType.Length; i++)
                    {
                        if (parameterEnumType[i] is TypeSyntax parameterType)
                        {
                            NameSyntax qualifiedParameterType = QualifiedName(nsSyntax, (SimpleNameSyntax)parameterType);
                            exposedParameterList = exposedParameterList.ReplaceNode(exposedParameterList.Parameters[i], exposedParameterList.Parameters[i].WithType(qualifiedParameterType.WithTrailingTrivia(Space)));
                            this.RequestInteropType(ns, parameterEnumType[i]!.ToString(), this.DefaultContext);
                            argumentList = argumentList.ReplaceNode(argumentList.Arguments[i], argumentList.Arguments[i].WithExpression(CastExpression(externDeclaration.ParameterList.Parameters[i].Type!.WithTrailingTrivia(default(SyntaxTriviaList)), argumentList.Arguments[i].Expression)));
                        }
                    }
                }

                // We need to specify Entrypoint because our local function will have a different name.
                // It must have a unique name because some functions will have the same signature as our exposed method except for the return type.
                entrypoint ??= methodName;
                IdentifierNameSyntax localExternFunctionName = IdentifierName("LocalExternFunction");
                ExpressionSyntax invocation = InvocationExpression(localExternFunctionName, argumentList);

                if (returnTypeEnumName is not null)
                {
                    this.RequestInteropType(ns, returnTypeEnumName.ToString(), this.DefaultContext);
                    returnTypeEnumName = QualifiedName(nsSyntax, (SimpleNameSyntax)returnTypeEnumName);
                    invocation = CastExpression(returnTypeEnumName, invocation);
                }

                BlockSyntax body = Block();

                IdentifierNameSyntax? retValLocalName = returnType.Type is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword } ? null : IdentifierName("__retVal");

                if (setLastErrorManually)
                {
                    // Marshal.SetLastSystemError(0);
                    body = body.AddStatements(ExpressionStatement(InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Marshal)), IdentifierName("SetLastSystemError")),
                        [Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0)))])));
                }

                if (retValLocalName is not null)
                {
                    // var __retVal = LocalExternFunction(...);
                    body = body.AddStatements(
                        LocalDeclarationStatement(
                            VariableDeclaration(returnTypeEnumName ?? returnType.Type, [VariableDeclarator(retValLocalName.Identifier, EqualsValueClause(invocation))])));
                }
                else
                {
                    // LocalExternFunction(...);
                    body = body.AddStatements(ExpressionStatement(invocation));
                }

                if (setLastErrorManually)
                {
                    // Marshal.SetLastPInvokeError(Marshal.GetLastSystemError())
                    body = body.AddStatements(ExpressionStatement(InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Marshal)), IdentifierName("SetLastPInvokeError")),
                        [Argument(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Marshal)), IdentifierName("GetLastSystemError"))))])));
                }

                if (retValLocalName is not null)
                {
                    // return __retVal;
                    body = body.AddStatements(ReturnStatement(retValLocalName));
                }

                LocalFunctionStatementSyntax externFunction = LocalFunctionStatement(externDeclaration.ReturnType, localExternFunctionName.Identifier)
                    .AddAttributeLists(CreateDllImportAttributeList().WithOpenBracketToken(Token(SyntaxKind.OpenBracketToken).WithLeadingTrivia(LineFeed)))
                    .WithModifiers(externDeclaration.Modifiers)
                    .WithParameterList(externDeclaration.ParameterList)
                    .WithSemicolonToken(SemicolonWithLineFeed);
                body = body.AddStatements(externFunction);

                exposedMethod = MethodDeclaration(returnTypeEnumName ?? returnType.Type, externDeclaration.Identifier)
                    .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword))
                    .WithParameterList(exposedParameterList)
                    .WithBody(body);
                if (requiresUnsafe)
                {
                    exposedMethod = exposedMethod.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
                }
            }

            // Partial is the last keyword if it's present.
            if (useGenerator)
            {
                exposedMethod = exposedMethod.AddModifiers(TokenWithSpace(SyntaxKind.PartialKeyword));
            }

            if (this.GetSupportedOSPlatformAttribute(methodDefinition.GetCustomAttributes()) is AttributeSyntax supportedOSPlatformAttribute)
            {
                exposedMethod = exposedMethod.AddAttributeLists(AttributeList(supportedOSPlatformAttribute));
            }

            // Add documentation if we can find it.
            exposedMethod = this.AddApiDocumentation(entrypoint ?? methodName, exposedMethod);

            this.volatileCode.AddMemberToModule(moduleName, this.DeclareFriendlyOverloads(methodDefinition, exposedMethod, this.methodsAndConstantsClassName, FriendlyOverloadOf.ExternMethod, this.injectedPInvokeHelperMethods, avoidWinmdRootAlias: false));
            this.volatileCode.AddMemberToModule(moduleName, exposedMethod);
        }
        catch (Exception ex) when (ex is not GenerationFailedException)
        {
            throw new GenerationFailedException($"Failed while generating extern method: {methodName}", ex);
        }
    }
}
